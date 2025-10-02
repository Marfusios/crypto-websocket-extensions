using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Hyperliquid.Client.Websocket.Client;
using Hyperliquid.Client.Websocket.Enums;
using Hyperliquid.Client.Websocket.Responses.Fills;
using Hyperliquid.Client.Websocket.Responses.Orders;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using Crypto.Websocket.Extensions.Core.Orders;

namespace Crypto.Websocket.Extensions.Orders.Sources
{
    /// <summary>
    /// Hyperliquid orders source
    /// </summary>
    public class HyperliquidOrderSource : OrderSourceBase
    {
        private readonly CryptoOrderCollection _partiallyFilledOrders = new();
        private HyperliquidWebsocketClient _client = null!;
        private IDisposable? _subscription;
        private IDisposable? _subscriptionFills;

        /// <inheritdoc />
        public HyperliquidOrderSource(HyperliquidWebsocketClient client) : base(client.Logger)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "hyperliquid";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(HyperliquidWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            _subscriptionFills?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.UserOrderUpdatesStream.Subscribe(HandleOrdersSafe);
            _subscriptionFills = _client.Streams.UserFillsStream.Subscribe(HandleFillsSafe);
        }

        private void HandleOrdersSafe(UserOrderResponse[] responses)
        {
            foreach (var response in responses)
            {
                try
                {
                    HandleOrder(response);
                }
                catch (Exception e)
                {
                    _client.Logger.LogError(e, "[Hyperliquid] Failed to handle order info status: {status}, id: {orderId}, error: '{error}'",
                        response.Status, response.Order.OrderId, e.Message);
                }
            }
        }

        private void HandleFillsSafe(UserFillsResponse response)
        {
            if (response.IsSnapshot == true)
            {
                try
                {
                    HandleFillsSnapshot(response.Fills);
                }
                catch (Exception e)
                {
                    _client.Logger.LogError(e, "[Hyperliquid] Failed to handle fills snapshot, error: '{error}'", e.Message);
                }

                return;
            }

            foreach (var fill in response.Fills)
            {
                try
                {
                    HandleFill(fill);
                }
                catch (Exception e)
                {
                    _client.Logger.LogError(e, "[Hyperliquid] Failed to handle fill info id: {orderId}, error: '{error}'",
                        fill.OrderId, e.Message);
                }
            }
        }

        private void HandleOrder(UserOrderResponse response)
        {
            if (response.Status is OrderStatus.Filled or OrderStatus.FilledCapitalized)
            {
                // there is another stream for filled orders, do nothing here
                return;
            }

            var order = ConvertOrder(response);
            OrderUpdatedSubject.OnNext(order);
        }

        private void HandleFill(Fill fill)
        {
            var order = ConvertFill(fill);
            OrderUpdatedSubject.OnNext(order);
        }

        private void HandleFillsSnapshot(Fill[] fills)
        {
            var orders = fills
                .Where(x => !ExistingOrders.ContainsKey(x.OrderId.ToString()))
                .Select(ConvertFill)
                .ToArray();
            if (orders.Length == 0)
                return;
            foreach (var order in orders)
                order.IsSnapshot = true;
            OrderSnapshotSubject.OnNext(orders);
        }

        /// <summary>
        /// Convert Binance order to crypto order
        /// </summary>
        public CryptoOrder ConvertOrder(UserOrderResponse response)
        {
            var order = response.Order;

            var id = order.OrderId.ToString();
            var existingCurrent = ExistingOrders.GetValueOrDefault(id);
            var existingPartial = _partiallyFilledOrders.GetValueOrDefault(id);
            var existing = existingPartial ?? existingCurrent;

            var price = Math.Abs(FirstNonZero(order.LimitPrice, existing?.Price) ?? 0);

            var currentStatus = ConvertOrderStatus(response);

            var newOrder = new CryptoOrder
            {
                Id = id,
                GroupId = existing?.GroupId,
                ClientId = order.ClientOrderId ?? existing?.ClientId,
                Pair = order.Coin ?? existing?.Pair ?? string.Empty,
                Side = order.Side == Side.Ask ? CryptoOrderSide.Ask : CryptoOrderSide.Bid,
                AmountFilled = 0,
                AmountFilledCumulative = existing?.AmountFilledCumulative,
                AmountOrig = FirstNonZero(order.OriginalSize, existing?.AmountOrig),
                AmountFilledQuote = 0,
                AmountFilledCumulativeQuote = existing?.AmountFilledCumulativeQuote,
                AmountOrigQuote = order.OriginalSize * order.LimitPrice,
                Created = order.Timestamp,
                Updated = response.StatusTimestamp,
                Price = price,
                PriceAverage = existing?.PriceAverage != null ? (existing.PriceGrouped + order.LimitPrice) * 0.5 : price,
                Fee = 0,
                FeeCurrency = null,
                OrderStatus = currentStatus,
                OrderStatusRaw = response.Status.ToString(),
                CanceledReason = null,
                Type = CryptoOrderType.Limit,
                TypePrev = existing?.TypePrev ?? existing?.Type ?? CryptoOrderType.Limit,
                OnMargin = existing?.OnMargin ?? true
            };

            return newOrder;
        }

        private CryptoOrder ConvertFill(Fill fill)
        {
            var id = fill.OrderId.ToString();
            var existingCurrent = ExistingOrders.GetValueOrDefault(id);
            var existingPartial = _partiallyFilledOrders.GetValueOrDefault(id);
            var existing = existingPartial ?? existingCurrent;

            var price = Math.Abs(FirstNonZero(fill.Price, existing?.Price) ?? 0);

            var currentStatus = (Math.Abs(existing?.AmountFilledCumulative ?? 0) + Math.Abs(fill.Size)) < Math.Abs(existing?.AmountOrig ?? 0) ? 
                CryptoOrderStatus.PartiallyFilled : 
                CryptoOrderStatus.Executed;

            var newOrder = new CryptoOrder
            {
                Id = id,
                GroupId = existing?.GroupId,
                ClientId = existing?.ClientId,
                Pair = fill.Coin ?? existing?.Pair ?? string.Empty,
                Side = fill.Side == Side.Ask ? CryptoOrderSide.Ask : CryptoOrderSide.Bid,
                AmountFilled = fill.Size,
                AmountFilledCumulative = Math.Abs(fill.Size) + Math.Abs(existing?.AmountFilledCumulative ?? 0),
                AmountOrig = existing?.AmountOrig ?? fill.Size,
                AmountFilledQuote = fill.Size * price,
                AmountFilledCumulativeQuote = Math.Abs(fill.Size) * fill.Price + Math.Abs(existing?.AmountFilledCumulativeQuote ?? 0),
                AmountOrigQuote = Math.Abs(existing?.AmountOrig ?? fill.Size) * price,
                Created = existing?.Created ?? fill.Time,
                Updated = fill.Time,
                Price = price,
                PriceAverage = existing?.PriceAverage != null ? (existing.PriceGrouped + fill.Price) * 0.5 : price,
                Fee = fill.Fee,
                FeeCurrency = fill.FeeToken,
                OrderStatus = currentStatus,
                OrderStatusRaw = fill.Direction,
                CanceledReason = null,
                Type = CryptoOrderType.Limit,
                TypePrev = existing?.TypePrev ?? existing?.Type ?? CryptoOrderType.Limit,
                OnMargin = existing?.OnMargin ?? true
            };

            if (currentStatus == CryptoOrderStatus.PartiallyFilled)
            {
                // save partially filled orders
                _partiallyFilledOrders[newOrder.Id] = newOrder;
            }
            else
            {
                _partiallyFilledOrders.TryRemove(newOrder.Id, out _);
            }

            return newOrder;
        }

        /// <summary>
        /// Convert order status
        /// </summary>
        public static CryptoOrderStatus ConvertOrderStatus(UserOrderResponse response)
        {
            var status = response.Status;
            switch (status)
            {
                case OrderStatus.Open:
                case OrderStatus.PlacedSuccessfully:
                    return CryptoOrderStatus.Active;
                case OrderStatus.FilledCapitalized:
                    return CryptoOrderStatus.Executed;
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
