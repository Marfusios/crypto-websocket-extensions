using System;
using System.Linq;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Responses.Orders;
using Coinbase.Client.Websocket.Responses.Trades;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Orders.Sources
{
    /// <inheritdoc />
    public class CoinbaseOrderSource : OrderSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();
        private readonly CryptoOrderCollection _partiallyFilledOrders = new CryptoOrderCollection();

        private CoinbaseWebsocketClient _client;
        private IDisposable _subscription;
        private IDisposable _subscriptionSnapshot;

        /// <inheritdoc />
        public CoinbaseOrderSource(CoinbaseWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "coinbase";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(CoinbaseWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.OrdersSnapshotStream.Subscribe(HandleSnapshot);
            _subscription = _client.Streams.OrderStream.Subscribe(HandleOrdersSafe);
        }

        private void HandleSnapshot(OrdersSnapshotResponse snapshot)
        {
            foreach (var order in snapshot.Orders)
            {
                HandleOrdersSafe(order);
            }
        }

        private void HandleOrdersSafe(OrderResponse response)
        {
            try
            {
                HandleOrder(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Coinbase] Failed to handle order info, error: '{e.Message}'");
            }
        }

        private void HandleOrder(OrderResponse response)
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
                case OrderStatus.Pending:
                case OrderStatus.Open:
                    OrderCreatedSubject.OnNext(orders);
                    break;
                case OrderStatus.Executed:
                case OrderStatus.PartiallyFilled:
                case OrderStatus.Canceled:
                case OrderStatus.All:
                case OrderStatus.Rejected:
                case OrderStatus.Done:
                case OrderStatus.Settled:
                    OrderUpdatedSubject.OnNext(orders);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private CryptoOrder[] ConvertOrders(OrderResponse[] data)
        {
            return data
                .Select(ConvertOrder)
                .ToArray();
        }

        private CryptoOrder ConvertOrder(OrderResponse order)
        {
            var id = order.Id.ToString();
            var clientId = order.ProfileId;
            var existingCurrent = ExistingOrders.ContainsKey(id) ? ExistingOrders[id] : null;
            var existingPartial = _partiallyFilledOrders.ContainsKey(id) ? _partiallyFilledOrders[id] : null;
            var existing = existingPartial ?? existingCurrent;

            var price = Math.Abs(FirstNonZero(order.Price, existing?.Price) ?? 0);

            var amount = Math.Abs(FirstNonZero(order.Amount, existing?.AmountOrig) ?? 0);

            var amountOrig = Math.Abs(order.Size ?? 0);

            var currentStatus = existing != null &&
                                existing.OrderStatus != CryptoOrderStatus.Undefined &&
                                existing.OrderStatus != CryptoOrderStatus.New &&
                                order.OrderStatus == OrderStatus.Undefined
                ? existing.OrderStatus
                : ConvertOrderStatus(order);

            var newOrder = new CryptoOrder

            {
                Id = id,
                Pair = CryptoPairsHelper.Clean(order.Pair),
                Price = price,
                //Amount = amount,
                AmountOrig = amountOrig,
                Side = ConvertSide(order.Side),
                OrderStatus = ConvertOrderStatus(order),
                Type = (CryptoOrderType) order.OrderType,
                Created = ConvertToDatetime(order.MtsCreate),
                ClientId = clientId
                //Created = Convert.ToDateTime(order.MtsCreate)
            };

            if (currentStatus == CryptoOrderStatus.PartiallyFilled)
            {
                // save partially filled orders
                _partiallyFilledOrders[newOrder.Id] = newOrder;
            }

            return newOrder;
        }

        private DateTime ConvertToDatetime(DateTimeOffset dateTimeOffset)
        {
            var sourceTime = new DateTimeOffset(dateTimeOffset.DateTime, TimeSpan.Zero);
            return sourceTime.DateTime;
        }

        private CryptoOrderStatus ConvertOrderStatus(OrderResponse user)
        {
            switch (user.OrderStatus)
            {
                case OrderStatus.Undefined:
                    break;
                case OrderStatus.All:
                    break;
                case OrderStatus.Pending:
                    return CryptoOrderStatus.Pending;

                case OrderStatus.Rejected:
                case OrderStatus.Canceled:
                    return CryptoOrderStatus.Canceled;

                case OrderStatus.PartiallyFilled:
                {
                    return user.Amount == 0 ? CryptoOrderStatus.Canceled : CryptoOrderStatus.PartiallyFilled;
                }

                case OrderStatus.Active:
                case OrderStatus.Open:
                    return CryptoOrderStatus.Active;

                case OrderStatus.Done:
                case OrderStatus.Settled:
                case OrderStatus.Executed:
                    return CryptoOrderStatus.Executed;

                default:
                    throw new ArgumentOutOfRangeException(nameof(user.OrderStatus), user.OrderStatus, null);
            }

            return CryptoOrderStatus.Undefined;
        }

        private CryptoOrderSide ConvertSide(TradeSide side)
        {
            if (side == TradeSide.Buy) return CryptoOrderSide.Bid;

            if (side == TradeSide.Sell) return CryptoOrderSide.Ask;

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
            if (!value.HasValue) return null;

            return Math.Abs(value.Value);
        }
    }
}