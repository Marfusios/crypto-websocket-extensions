using System;
using System.Linq;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Responses;
using Bitmex.Client.Websocket.Responses.Orders;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Orders.Sources
{
    /// <summary>
    /// Bitmex orders source
    /// </summary>
    public class BitmexOrderSource : OrderSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly CryptoOrderCollection _partiallyFilledOrders = new CryptoOrderCollection();
        private BitmexWebsocketClient _client;
        private IDisposable _subscription;

        /// <inheritdoc />
        public BitmexOrderSource(BitmexWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitmex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitmexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.OrderStream.Subscribe(HandleOrdersSafe);
        }

        private void HandleOrdersSafe(OrderResponse response)
        {
            try
            {
                HandleOrders(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Bitmex] Failed to handle order info, error: '{e.Message}'");
            }
        }

        private void HandleOrders(OrderResponse response)
        {
            if (response?.Data == null || !response.Data.Any())
            {
                // weird state, do nothing
                return;
            }

            var orders = ConvertOrders(response.Data);

            if (response.Action == BitmexAction.Partial)
            {
                // received snapshot, stream
                OrderSnapshotSubject.OnNext(orders);
                return;
            }

            foreach (var order in orders)
            {
                if (response.Action == BitmexAction.Insert)
                    OrderCreatedSubject.OnNext(order);
                else
                    OrderUpdatedSubject.OnNext(order);
            }
        }

        /// <summary>
        /// Convert Bitmex orders to crypto orders
        /// </summary>
        public CryptoOrder[] ConvertOrders(Order[] orders)
        {
            return orders
                .Select(ConvertOrder)
                .ToArray();
        }

        /// <summary>
        /// Convert Bitmex order to crypto order
        /// </summary>
        public CryptoOrder ConvertOrder(Order order)
        {
            var id = order.OrderId;
            var existingCurrent = ExistingOrders.ContainsKey(id) ? ExistingOrders[id] : null;
            var existingPartial = _partiallyFilledOrders.ContainsKey(id) ? _partiallyFilledOrders[id] : null;
            var existing = existingPartial ?? existingCurrent;

            var price = Math.Abs(FirstNonZero(order.Price, order.AvgPx, existing?.Price) ?? 0);
            var priceAvg = Math.Abs(FirstNonZero(order.AvgPx, order.Price, existing?.PriceAverage) ?? 0);

            var isPartial = order.OrdStatus == OrderStatus.Undefined ?
                existing?.OrderStatus == CryptoOrderStatus.PartiallyFilled :
                order.OrdStatus == OrderStatus.PartiallyFilled;

            var beforePartialFilledAmount =
                existing?.OrderStatus == CryptoOrderStatus.PartiallyFilled ? Abs(existing.AmountFilledCumulative) : 0;

            var beforePartialFilledAmountQuote =
                existing?.OrderStatus == CryptoOrderStatus.PartiallyFilled ? Abs(existing.AmountFilledCumulativeQuote) : 0;

            var orderQtyQuote = Abs(order.OrderQty ?? existing?.AmountOrigQuote);
            var cumQtyQuote = Abs(order.CumQty ?? existing?.AmountFilledCumulativeQuote);
            var leavesQtyQuote = Abs(order.LeavesQty);

            var orderQtyBase = Abs(ConvertFromContracts(orderQtyQuote, price));
            var cumQtyBase = Abs(ConvertFromContracts(cumQtyQuote, price));
            var leavesQtyBase = Abs(ConvertFromContracts(order.LeavesQty, price));

            var amountFilledCumulative = FirstNonZero(
                    cumQtyBase,
                    existing?.AmountFilledCumulative);

            var amountFilledCumulativeQuote = FirstNonZero(
                cumQtyQuote,
                existing?.AmountFilledCumulativeQuote);

            var amountFilled = FirstNonZero(
                    cumQtyBase - beforePartialFilledAmount, // Bitmex doesn't send partial difference when filled
                    existing?.AmountFilled);

            var amountFilledQuote = FirstNonZero(
                cumQtyQuote - beforePartialFilledAmountQuote, // Bitmex doesn't send partial difference when filled
                existing?.AmountFilledQuote);

            var amountOrig = isPartial
                ? (cumQtyBase ?? 0) + (leavesQtyBase ?? 0)
                : FirstNonZero(
                    orderQtyBase,
                    existing?.AmountOrig);

            var amountOrigQuote = isPartial
                ? (cumQtyQuote ?? 0) + (leavesQtyQuote ?? 0)
                : FirstNonZero(
                    orderQtyQuote,
                    existing?.AmountOrigQuote);

            if (order.Side == BitmexSide.Undefined && existing != null)
            {
                order.Side = existing.AmountGrouped < 0 ? BitmexSide.Sell : BitmexSide.Buy;
            }

            var currentStatus = existing != null &&
                                existing.OrderStatus != CryptoOrderStatus.Undefined && 
                                existing.OrderStatus != CryptoOrderStatus.New &&
                                order.OrdStatus == OrderStatus.Undefined ?
                existing.OrderStatus :
                ConvertOrderStatus(order);

            var newOrder = new CryptoOrder
            {
                Id = id,
                GroupId = existing?.GroupId ?? null,
                ClientId = !string.IsNullOrWhiteSpace(order.ClOrdId) ? 
                    order.ClOrdId :
                    existing?.ClientId,
                Pair = order.Symbol ?? existing?.Pair,
                Side = order.Side == BitmexSide.Sell ? CryptoOrderSide.Ask : CryptoOrderSide.Bid,
                AmountFilled = amountFilled,
                AmountFilledCumulative = amountFilledCumulative,
                AmountOrig = amountOrig,
                AmountFilledQuote = amountFilledQuote,
                AmountFilledCumulativeQuote = amountFilledCumulativeQuote,
                AmountOrigQuote = amountOrigQuote,
                Created = order.TransactTime ?? existing?.Created,
                Updated = order.Timestamp ?? existing?.Updated,
                Price = price,
                PriceAverage = priceAvg,
                OrderStatus = currentStatus,
                Type = existing?.Type ?? ConvertOrderType(order.OrdType),
                TypePrev = existing?.TypePrev ?? ConvertOrderType(order.OrdType),
                OnMargin = existing?.OnMargin ?? false
            };


            if (currentStatus == CryptoOrderStatus.PartiallyFilled)
            {
                // save partially filled orders
                _partiallyFilledOrders[newOrder.Id] = newOrder;
            }

            return newOrder;
        }


        /// <summary>
        /// Convert order type
        /// </summary>
        public static CryptoOrderType ConvertOrderType(string type)
        {
            var typeSafe = (type ?? string.Empty).ToLower();

            switch (typeSafe)
            {
                case "market":
                    return CryptoOrderType.Market;
                case "stop":
                    return CryptoOrderType.Stop;
                case "stoplimit":
                    return CryptoOrderType.StopLimit;
                case "limit":
                    return CryptoOrderType.Limit;
                case "limitiftouched":
                    return CryptoOrderType.TakeProfitLimit;
                case "marketiftouched":
                    return CryptoOrderType.TakeProfitMarket;
                default:
                    return CryptoOrderType.Undefined;
            }
        }

        /// <summary>
        /// Convert to base from contracts
        /// </summary>
        public static double? ConvertFromContracts(long? contracts, double price)
        {
            return contracts / price;
        }

        /// <summary>
        /// Convert to base from contracts
        /// </summary>
        public static double ConvertFromContracts(long contracts, double price)
        {
            return contracts / price;
        }

        /// <summary>
        /// Convert to base from contracts
        /// </summary>
        public static double? ConvertFromContracts(double? contracts, double price)
        {
            return contracts / price;
        }

        /// <summary>
        /// Convert order status
        /// </summary>
        public static CryptoOrderStatus ConvertOrderStatus(Order order)
        {
            var status = order.OrdStatus;
            switch (status)
            {
                case OrderStatus.New:
                    return order.WorkingIndicator ?? false ? CryptoOrderStatus.Active : CryptoOrderStatus.New;
                case OrderStatus.PartiallyFilled:
                    return CryptoOrderStatus.PartiallyFilled;
                case OrderStatus.Filled:
                    return CryptoOrderStatus.Executed;
                case OrderStatus.Undefined:
                    return order.WorkingIndicator ?? false ? CryptoOrderStatus.Active : CryptoOrderStatus.Canceled;
                default:
                    return CryptoOrderStatus.Canceled;
            }
        }


        private static double? FirstNonZero(params double?[] numbers)
        {
            foreach (var number in numbers)
            {
                if (number.HasValue && Math.Abs(number.Value) > 0)
                    return number.Value;
            }

            return null;
        }

        private static double? Abs(double? value)
        {
            if (!value.HasValue)
                return null;
            return Math.Abs(value.Value);
        }
    }
}
