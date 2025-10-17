using System;
using System.Collections.Generic;
using System.Linq;
using Aster.Client.Websocket.Client;
using Aster.Client.Websocket.Responses.Orders;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Orders.Sources
{
    /// <summary>
    /// Aster orders source
    /// </summary>
    public class AsterOrderSource : OrderSourceBase
    {
        private readonly CryptoOrderCollection _partiallyFilledOrders = new();
        private AsterWebsocketClient _client = null!;
        private IDisposable? _subscription;

        /// <inheritdoc />
        public AsterOrderSource(AsterWebsocketClient client) : base(client.Logger)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "aster";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(AsterWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.OrderUpdateStream.Subscribe(HandleOrdersSafe);
        }

        private void HandleOrdersSafe(OrderUpdate response)
        {
            try
            {
                HandleOrders(response);
            }
            catch (Exception e)
            {
                _client.Logger.LogError(e, "[Binance] Failed to handle order info, error: '{error}'", e.Message);
            }
        }

        private void HandleOrders(OrderUpdate response)
        {
            var order = ConvertOrder(response);
            OrderUpdatedSubject.OnNext(order);
        }

        /// <summary>
        /// Convert Binance orders to crypto orders
        /// </summary>
        public CryptoOrder[] ConvertOrders(OrderUpdate[] orders)
        {
            return orders
                .Select(ConvertOrder)
                .ToArray();
        }

        /// <summary>
        /// Convert Binance order to crypto order
        /// </summary>
        public CryptoOrder ConvertOrder(OrderUpdate orderUpdate)
        {
            var order = orderUpdate.Order;

            var id = order.OrderId.ToString();
            var existingCurrent = ExistingOrders.GetValueOrDefault(id);
            var existingPartial = _partiallyFilledOrders.GetValueOrDefault(id);
            var existing = existingPartial ?? existingCurrent;

            var price = Math.Abs(FirstNonZero(order.LastPriceFilled, order.Price, existing?.Price) ?? 0);
            var priceAvg = Math.Abs(FirstNonZero(order.AveragePrice, order.LastPriceFilled, order.Price, existing?.PriceAverage) ?? 0);

            var amountQuote = FirstNonZero(order.Quantity * order.Price, existing?.AmountOrigQuote);
            var amountFilledQuote = FirstNonZero(order.LastQuantityFilled * order.LastPriceFilled);
            var amountFilledQuoteCumulative = FirstNonZero(order.QuantityFilled * priceAvg, existing?.AmountFilledCumulativeQuote);

            var currentStatus = ConvertOrderStatus(order);

            var newOrder = new CryptoOrder
            {
                Id = id,
                GroupId = existing?.GroupId ?? null,
                ClientId = !string.IsNullOrWhiteSpace(order.ClientOrderId) ?
                    order.ClientOrderId : existing?.ClientId,
                Pair = order.Symbol ?? existing?.Pair ?? string.Empty,
                Side = order.Side == OrderSide.Sell ? CryptoOrderSide.Ask : CryptoOrderSide.Bid,
                AmountFilled = order.LastQuantityFilled,
                AmountFilledCumulative = order.QuantityFilled,
                AmountOrig = FirstNonZero(order.Quantity, existing?.AmountOrig),
                AmountFilledQuote = amountFilledQuote,
                AmountFilledCumulativeQuote = amountFilledQuoteCumulative,
                AmountOrigQuote = amountQuote,
                Created = order.OrderTradeTime ?? existing?.Created,
                Updated = orderUpdate.TransactionTime ?? orderUpdate.EventTime,
                Price = price,
                PriceAverage = priceAvg,
                Fee = order.CommissionAmount ?? 0,
                FeeCurrency = order.CommissionAsset,
                OrderStatus = currentStatus,
                OrderStatusRaw = order.Status.ToString(),
                CanceledReason = order.ExecutionType.ToString(),
                Type = ConvertOrderType(order.Type) ?? existing?.Type ?? CryptoOrderType.Undefined,
                TypePrev = existing?.TypePrev ?? existing?.Type ?? ConvertOrderType(order.Type) ?? CryptoOrderType.Undefined,
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
        public static CryptoOrderType? ConvertOrderType(OrderType type)
        {
            switch (type)
            {
                case OrderType.Market:
                    return CryptoOrderType.Market;
                case OrderType.StopLoss:
                    return CryptoOrderType.Stop;
                case OrderType.StopLossLimit:
                    return CryptoOrderType.StopLimit;
                case OrderType.Limit:
                case OrderType.LimitMaker:
                    return CryptoOrderType.Limit;
                case OrderType.TakeProfitLimit:
                    return CryptoOrderType.TakeProfitLimit;
                case OrderType.TakeProfit:
                    return CryptoOrderType.TakeProfitMarket;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Convert order status
        /// </summary>
        public static CryptoOrderStatus ConvertOrderStatus(OrderUpdateData order)
        {
            var status = order.Status;
            switch (status)
            {
                // They for some reason send New status for partially filled orders
                case OrderStatus.New when Math.Abs(order.LastQuantityFilled) > 0:
                    return CryptoOrderStatus.PartiallyFilled;
                case OrderStatus.New:
                    return CryptoOrderStatus.Active;
                case OrderStatus.PartiallyFilled:
                    return CryptoOrderStatus.PartiallyFilled;
                case OrderStatus.Filled:
                    return CryptoOrderStatus.Executed;
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
