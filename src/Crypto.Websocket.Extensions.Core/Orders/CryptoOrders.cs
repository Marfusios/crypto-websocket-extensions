using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Crypto.Websocket.Extensions.Core.Logging;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Core.Validations;

namespace Crypto.Websocket.Extensions.Core.Orders
{
    /// <summary>
    /// Orders manager
    /// </summary>
    public class CryptoOrders : ICryptoOrders
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly ICryptoOrderSource _source;
        private readonly int? _orderPrefix;
        private readonly Subject<CryptoOrder> _orderChanged = new Subject<CryptoOrder>();
        private readonly Subject<CryptoOrder> _ourOrderChanged = new Subject<CryptoOrder>();
        private readonly CryptoOrderCollection _idToOrder = new CryptoOrderCollection();

        private long _cidCounter = 0;

        /// <summary>
        /// Orders manager
        /// </summary>
        /// <param name="source">Orders source</param>
        /// <param name="orderPrefix">Select prefix if you want to distinguish orders</param>
        public CryptoOrders(ICryptoOrderSource source, int? orderPrefix = null)
        {
            CryptoValidations.ValidateInput(source, nameof(source));

            _source = source;
            _orderPrefix = orderPrefix;

            Subscribe();
        }

        /// <summary>
        /// Order was changed stream
        /// </summary>
        public IObservable<CryptoOrder> OrderChangedStream => _orderChanged.AsObservable();

        /// <summary>
        /// Order was changed stream (only ours, based on client id prefix)
        /// </summary>
        public IObservable<CryptoOrder> OurOrderChangedStream => _ourOrderChanged.AsObservable();

        /// <summary>
        /// Selected client id prefix
        /// </summary>
        public long? ClientIdPrefix => _orderPrefix * 1000000;


        /// <summary>
        /// Selected client id prefix as string
        /// </summary>
        public string ClientIdPrefixString => ClientIdPrefix?.ToString() ?? string.Empty;

        /// <summary>
        /// Last executed (or partially filled) buy order
        /// </summary>
        public CryptoOrder LastExecutedBuyOrder { get; private set; }

        /// <summary>
        /// Last executed (or partially filled) sell order
        /// </summary>
        public CryptoOrder LastExecutedSellOrder { get; private set; }


        /// <summary>
        /// Generate a new client id (with prefix)
        /// </summary>
        public long GenerateClientId()
        {
            return Interlocked.Increment(ref _cidCounter);
        }

        /// <summary>
        /// Returns only our active orders (based on client id prefix)
        /// </summary>
        public CryptoOrderCollectionReadonly GetOurActiveOrders()
        {
            var orders = _idToOrder
                .Where(x =>
                    IsOurOrder(x.Value.ClientId) &&
                    x.Value.OrderStatus != CryptoOrderStatus.Canceled &&
                    x.Value.OrderStatus != CryptoOrderStatus.Executed &&
                    x.Value.OrderStatus != CryptoOrderStatus.Undefined)
                .ToDictionary(x => x.Key, y => y.Value);
            return new CryptoOrderCollectionReadonly(orders);
        }

        /// <summary>
        /// Returns only our orders (based on client id prefix)
        /// </summary>
        public CryptoOrderCollectionReadonly GetOurOrders()
        {
            var orders = _idToOrder
                .Where(x => IsOurOrder(x.Value.ClientId))
                .ToDictionary(x => x.Key, y => y.Value);
            return new CryptoOrderCollectionReadonly(orders);
        }

        /// <summary>
        /// Returns all orders
        /// </summary>
        public CryptoOrderCollectionReadonly GetOrders()
        {
            var orders = _idToOrder
                .ToDictionary(x => x.Key, y => y.Value);
            return new CryptoOrderCollectionReadonly(orders);
        }

        /// <summary>
        /// Find active order by provided unique id
        /// </summary>
        public CryptoOrder FindActiveOrder(string id)
        {
            if (GetOurActiveOrders().ContainsKey(id))
                return _idToOrder[id];
            return null;
        }

        /// <summary>
        /// Find order by provided unique id
        /// </summary>
        public CryptoOrder FindOrder(string id)
        {
            if (_idToOrder.ContainsKey(id))
                return _idToOrder[id];
            return null;
        }

        /// <summary>
        /// Find active order by provided client id
        /// </summary>
        public CryptoOrder FindActiveOrderByClientId(string clientId)
        {
            var item = GetOurActiveOrders().FirstOrDefault(x => x.Value.ClientId == clientId);
            return item.Value;
        }

        /// <summary>
        /// Find order by provided client id
        /// </summary>
        public CryptoOrder FindOrderByClientId(string clientId)
        {
            var item = _idToOrder.FirstOrDefault(x => x.Value.ClientId == clientId);
            return item.Value;
        }

        /// <summary>
        /// Returns true if client id matches prefix
        /// </summary>
        public bool IsOurOrder(CryptoOrder order)
        {
            return IsOurOrder(order.ClientId);
        }

        /// <summary>
        /// Returns true if client id matches prefix
        /// </summary>
        public bool IsOurOrder(string clientId)
        {
            return clientId.StartsWith(ClientIdPrefixString);
        }



        private void Subscribe()
        {
            _source.OrdersInitialStream.Subscribe(OnOrdersUpdated);
            _source.OrderCreatedStream.Subscribe(OnOrderCreated);
            _source.OrderUpdatedStream.Subscribe(OnOrderUpdated);
        }

        private void OnOrdersUpdated(CryptoOrder[] orders)
        {
            if (orders == null) 
                return;

            foreach (var order in orders)
            {
                OnOrderUpdated(order);
            }
        }

        private void OnOrderCreated(CryptoOrder order)
        {
            if (order == null) 
                return;

            _idToOrder[order.Id] = order;
            HandleOrderUpdated(order);
        }

        private void OnOrderUpdated(CryptoOrder order)
        {
            if (order == null) 
                return;

            if (order.OrderStatus == CryptoOrderStatus.Undefined)
            {
                Log.Debug($"[ORDERS] Received order with weird status ({order.ClientId} - {order.PriceGrouped}/{order.AmountGrouped})");
            }

            if (order.OrderStatus == CryptoOrderStatus.Canceled || order.OrderStatus == CryptoOrderStatus.Undefined)
            {
                if (_idToOrder.ContainsKey(order.Id))
                {
                    _idToOrder.TryRemove(order.Id, out CryptoOrder _);
                }
            }
            else
            {
                _idToOrder[order.Id] = order;
            }
            HandleOrderUpdated(order);
        }

        private void HandleOrderUpdated(CryptoOrder order)
        {
            if (order.OrderStatus == CryptoOrderStatus.Executed || order.OrderStatus == CryptoOrderStatus.PartiallyFilled)
            {
                if (order.Side == CryptoOrderSide.Ask)
                    LastExecutedSellOrder = order;
                else
                    LastExecutedBuyOrder = order;
            }

            _orderChanged.OnNext(order);

            if(IsOurOrder(order))
                _ourOrderChanged.OnNext(order);
        }
    }
}
