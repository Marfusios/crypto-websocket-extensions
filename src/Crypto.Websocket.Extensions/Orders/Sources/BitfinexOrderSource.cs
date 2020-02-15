using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Responses.Orders;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;
using System;
using System.Linq;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Models;

namespace Crypto.Websocket.Extensions.Orders.Sources
{
    /// <inheritdoc />
    public class BitfinexOrderSource : OrderSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly CryptoOrderCollection _partiallyFilledOrders = new CryptoOrderCollection();

        private BitfinexWebsocketClient _client;
        private IDisposable _subscriptionCanceled;
        private IDisposable _subscriptionCreated;
        private IDisposable _subscriptionSnapshot;
        private IDisposable _subscriptionUpdated;

        /// <inheritdoc />
        public BitfinexOrderSource(BitfinexWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitfinex";

        /// <summary>
        ///     Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitfinexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionCanceled?.Dispose();
            _subscriptionCreated?.Dispose();
            _subscriptionUpdated?.Dispose();
            _subscriptionSnapshot?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscriptionCanceled = _client.Streams.OrderCanceledStream.Subscribe(HandleOrdersSafe);
            _subscriptionCreated = _client.Streams.OrderCreatedStream.Subscribe(HandleOrdersSafe);
            _subscriptionUpdated = _client.Streams.OrderUpdatedStream.Subscribe(HandleOrdersSafe);
            _subscriptionSnapshot = _client.Streams.OrdersStream.Subscribe(HandleSnapshotSafe);
        }

        private void HandleSnapshotSafe(Order[] orders)
        {
            try
            {
                OrderSnapshotSubject.OnNext(ConvertOrders(orders));
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Bitmex] Failed to handle order info, error: '{e.Message}'");
            }
        }

        private void HandleOrdersSafe(Order response)
        {
            try
            {
                HandleOrder(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Bitmex] Failed to handle order info, error: '{e.Message}'");
            }
        }

        private void HandleOrder(Order response)
        {
            if (response == null)
            {
                // weird state, do nothing
                return;
            }

            var orders = ConvertOrder(response);

            switch (response.OrderStatus)
            {
                case OrderStatus.Undefined:
                    break;
                case OrderStatus.Active:
                    OrderCreatedSubject.OnNext(orders);
                    break;
                case OrderStatus.Executed:
                case OrderStatus.PartiallyFilled:
                case OrderStatus.Canceled:
                case OrderStatus.PostOnlyCanceled:
                case OrderStatus.RsnPosReduceFlip:
                case OrderStatus.RsnPosReduceIncr:
                case OrderStatus.InsufficientBalance:
                case OrderStatus.InsufficientMargin:
                    OrderUpdatedSubject.OnNext(orders);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private CryptoOrder[] ConvertOrders(Order[] orders)
        {
            return orders
                .Select(ConvertOrder)
                .ToArray();
        }

        private CryptoOrder ConvertOrder(Order order)
        {
            var id = order.Id.ToString();
            var existingCurrent = ExistingOrders.ContainsKey(id) ? ExistingOrders[id] : null;
            var existingPartial = _partiallyFilledOrders.ContainsKey(id) ? _partiallyFilledOrders[id] : null;
            var existing = existingPartial ?? existingCurrent;

            var price = Math.Abs(FirstNonZero(order.Price, existing?.Price) ?? 0);

            var amount = Math.Abs(FirstNonZero(order.Amount, existing?.AmountOrig) ?? 0);

            var amountOrig = Math.Abs(order.AmountOrig ?? 0);

            var currentStatus = existing != null &&
                                existing.OrderStatus != CryptoOrderStatus.Undefined &&
                                existing.OrderStatus != CryptoOrderStatus.New &&
                                order.OrderStatus == OrderStatus.Undefined
                ? existing.OrderStatus
                : ConvertOrderStatus(order);

            var newOrder = new CryptoOrder
            {
                //ExchangeName = ExchangeName,

                Pair = order.Symbol ?? existing?.Pair,
                Price = price,
                //Amount = amount,
                AmountOrig = amountOrig,
                Side = ConvertSide(Convert.ToDouble(order.Amount)),
                Id = id,
                ClientId = order.Cid.ToString(),
                OrderStatus = ConvertOrderStatus(order.OrderStatus),
                Type = ConvertOrderType(order.Type.ToString()),
                Created = order.MtsCreate,
                Updated = order.MtsUpdate
            };

            if (currentStatus == CryptoOrderStatus.PartiallyFilled)
            {
                // save partially filled orders
                _partiallyFilledOrders[newOrder.Id] = newOrder;
            }

            return newOrder;
        }

        public static CryptoOrderType ConvertOrderType(string type)
        {
            var typeSafe = (type ?? string.Empty).ToLower();

            switch (typeSafe)
            {
                case "market":
                case "exchangemarket":
                    return CryptoOrderType.Market;
                case "stop":
                case "exchangestop":
                    return CryptoOrderType.Stop;
                case "stoplimit":
                case "exchangestoplimit":
                    return CryptoOrderType.StopLimit;
                case "limit":
                case "exchangelimit":
                    return CryptoOrderType.Limit;
                case "fok":
                case "exchangefok":
                    return CryptoOrderType.Fok;
                case "trailingstop":
                case "exchangetrailingstop":
                    return CryptoOrderType.TrailingStop;
                default:
                    return CryptoOrderType.Undefined;
            }
        }

        private CryptoOrderStatus ConvertOrderStatus(OrderStatus status)
        {
            switch (status)
            {
                case OrderStatus.Undefined:
                    break;

                case OrderStatus.Canceled:
                    return CryptoOrderStatus.Canceled;

                case OrderStatus.PartiallyFilled:
                    return CryptoOrderStatus.PartiallyFilled;

                case OrderStatus.Active:
                    return CryptoOrderStatus.Active;

                case OrderStatus.Executed:
                    return CryptoOrderStatus.Executed;

                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }

            return CryptoOrderStatus.Undefined;
        }

        private CryptoOrderSide ConvertSide(double amount)
        {
            if (amount > 0) return CryptoOrderSide.Bid;

            if (amount < 0) return CryptoOrderSide.Ask;

            return CryptoOrderSide.Undefined;
        }

        private static double? FirstNonZero(params double?[] numbers)
        {
            foreach (var number in numbers)
                if (number.HasValue && Math.Abs(number.Value) > 0)
                    return number.Value;

            return null;
        }

        private static double? Abs(double? value)
        {
            if (!value.HasValue)
                return null;
            return Math.Abs(value.Value);
        }

        public static CryptoOrderStatus ConvertOrderStatus(Order order)
        {
            var status = order.OrderStatus;
            switch (status)
            {
                case OrderStatus.Undefined:
                    Log.Error($"Failed to parse order status '{order.Pair}' --> OrderStatus: {order.OrderStatus}");
                    return CryptoOrderStatus.Canceled;
                case OrderStatus.Active:
                    return CryptoOrderStatus.Active;
                case OrderStatus.Canceled:
                    return CryptoOrderStatus.Canceled;
                case OrderStatus.PartiallyFilled:
                    return CryptoOrderStatus.PartiallyFilled;
                case OrderStatus.Executed:
                    return CryptoOrderStatus.Executed;
                default:
                    return CryptoOrderStatus.Canceled;
            }
        }
    }
}