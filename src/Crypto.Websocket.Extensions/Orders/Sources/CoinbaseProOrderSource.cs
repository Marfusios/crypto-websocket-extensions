using CoinbasePro.Client.Websocket.Client;
using CoinbasePro.Client.Websocket.Models.Users;
using CoinbasePro.Client.Websocket.Types;


using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;
using System;
using System.Linq;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using CryptoOrder = Crypto.Websocket.Extensions.Core.Orders.CryptoOrder;
using OrderStatus = CoinbasePro.Client.Websocket.Types.OrderStatus;

namespace Crypto.Websocket.Extensions.Orders.Sources
{
    /// <inheritdoc />
    public class CoinbaseProOrderSource : OrderSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();
        private readonly CryptoOrderCollection _partiallyFilledOrders = new CryptoOrderCollection();

        private CoinbaseProClient _client;
        private IDisposable _subscription;

        /// <inheritdoc />
        public CoinbaseProOrderSource(CoinbaseProClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            Subscribe();
        }

        /// <inheritdoc />
        public override string ExchangeName => "coinbasePro";
        
        /// <summary>
        ///     Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(CoinbaseProClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }
        
        private void Subscribe()
        {
            _subscription = _client.Streams.User.Subscribe(HandleOrdersSafe);
        }

        private void HandleOrdersSafe(User response)
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
        
        private void HandleOrder(User response)
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

        private CryptoWallet[] ConvertOrders(User[] data)
        {
            return data
                .Select(ConvertOrder)
                .ToArray();
        }
        
        private CryptoOrder ConvertOrder(User order)
        {
            var id = order.Id.ToString();
            var existingCurrent = ExistingOrders.ContainsKey(id) ? ExistingOrders[id] : null;
            var existingPartial = _partiallyFilledOrders.ContainsKey(id) ? _partiallyFilledOrders[id] : null;
            var existing = existingPartial ?? existingCurrent;
            
            var price = Math.Abs(FirstNonZero(order.Price, existing?.Price) ?? 0);

            var amount = Math.Abs(FirstNonZero(order.Amount, existing?.AmountOrig) ?? 0);

            var amountOrig = Math.Abs(order.Size ?? 0);
            
            var currentStatus = existing != null &&
                                existing.OrderStatus != CryptoOrderStatus.Undefined && 
                                existing.OrderStatus != CryptoOrderStatus.New &&
                                order.OrderStatus == OrderStatus.Undefined ?
                existing.OrderStatus :
                ConvertOrderStatus(order);
            
            var newOrder = new CryptoOrder
            
            {
                Pair = CryptoPairsHelper.Clean(order.Pair),
                Price = price,
                Amount = amount,
                AmountOrig = amountOrig,
                Side = ConvertSide(order.Side),
                OrderStatus = ConvertOrderStatus(order),
                Type = (CryptoOrderType)order.OrderType,
                Created = Convert.ToDateTime(order.MtsCreate)
            };
            
            if (currentStatus == CryptoOrderStatus.PartiallyFilled)
            {
                // save partially filled orders
                _partiallyFilledOrders[newOrder.Id] = newOrder;
            }

            return newOrder;
        }
        
        private CryptoOrderStatus ConvertOrderStatus(User user)
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

        private CryptoOrderSide ConvertSide(OrderSide side)
        {
            if (side == OrderSide.Buy)
            {
                return CryptoOrderSide.Bid;
            }

            if (side == OrderSide.Sell)
            {
                return CryptoOrderSide.Ask;
            }

            return CryptoOrderSide.Undefined;
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