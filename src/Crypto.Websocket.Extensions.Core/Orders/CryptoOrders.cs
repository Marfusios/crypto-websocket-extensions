using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Core.Orders
{
    /// <summary>
    /// Orders manager
    /// </summary>
    public class CryptoOrders : ICryptoOrders
    {
        private readonly IOrderSource _source;
        private readonly long? _orderPrefix;
        private readonly Subject<CryptoOrder> _orderChanged = new Subject<CryptoOrder>();
        private readonly Subject<CryptoOrder> _ourOrderChanged = new Subject<CryptoOrder>();
        private readonly IObservable<CryptoOrder> _orderChangedStream;
        private readonly IObservable<CryptoOrder> _ourOrderChangedStream;
        private readonly CryptoOrderCollection _idToOrder = new CryptoOrderCollection();

        private long _cidCounter;

        /// <summary>
        /// Orders manager
        /// </summary>
        /// <param name="source">Orders source</param>
        /// <param name="orderPrefix">Select prefix if you want to distinguish orders</param>
        /// <param name="targetPair">Select target pair, if you want to filter monitored orders</param>
        public CryptoOrders(IOrderSource source, long? orderPrefix = null, string? targetPair = null)
        {
            CryptoValidations.ValidateInput(source, nameof(source));

            _source = source;
            TargetPair = CryptoPairsHelper.Clean(targetPair);
            TargetPairOriginal = targetPair;
            _source.SetExistingOrders(_idToOrder);
            _orderPrefix = orderPrefix;
            _orderChangedStream = _orderChanged.AsObservable();
            _ourOrderChangedStream = _ourOrderChanged.AsObservable();

            Subscribe();
        }

        /// <summary>
        /// Order was changed stream
        /// </summary>
        public IObservable<CryptoOrder> OrderChangedStream => _orderChangedStream;

        /// <summary>
        /// Order was changed stream (only ours, based on client id prefix)
        /// </summary>
        public IObservable<CryptoOrder> OurOrderChangedStream => _ourOrderChangedStream;

        /// <summary>
        /// Selected client id prefix
        /// </summary>
        public long? ClientIdPrefix => _orderPrefix * ClientIdPrefixExponent;

        /// <summary>
        /// Client id exponent when prefix is selected.
        /// For example:
        ///   prefix = 333
        ///   exponent = 1000000
        ///   generated client id = 333000001
        /// </summary>
        public long ClientIdPrefixExponent { get; set; } = 10000000;


        /// <summary>
        /// Selected client id prefix as string
        /// </summary>
        public string ClientIdPrefixString => _orderPrefix?.ToString() ?? string.Empty;

        /// <summary>
        /// Target pair for this orders data (other orders will be filtered out)
        /// </summary>
        public string TargetPair { get; private set; }

        /// <summary>
        /// Originally provided target pair for this orders data
        /// </summary>
        public string? TargetPairOriginal { get; private set; }

        /// <summary>
        /// Last executed (or partially filled) buy order
        /// </summary>
        public CryptoOrder? LastExecutedBuyOrder { get; private set; }

        /// <summary>
        /// Last executed (or partially filled) sell order
        /// </summary>
        public CryptoOrder? LastExecutedSellOrder { get; private set; }


        /// <summary>
        /// Generate a new client id (with prefix)
        /// </summary>
        public long GenerateClientId()
        {
            var counter = Interlocked.Increment(ref _cidCounter);
            if (ClientIdPrefix.HasValue)
                return ClientIdPrefix.Value + counter;
            return counter;
        }

        /// <summary>
        /// Returns only our active orders (based on client id prefix)
        /// </summary>
        public CryptoOrderCollectionReadonly GetActiveOrders()
        {
            var orders = new Dictionary<string, CryptoOrder>(_idToOrder.Count);
            foreach (var item in _idToOrder)
            {
                if (IsOurOrder(item.Value.ClientId) && IsActive(item.Value))
                    orders[item.Key] = item.Value;
            }

            return new CryptoOrderCollectionReadonly(orders);
        }

        /// <summary>
        /// Returns only our orders (based on client id prefix)
        /// </summary>
        public CryptoOrderCollectionReadonly GetOrders()
        {
            var orders = new Dictionary<string, CryptoOrder>(_idToOrder.Count);
            foreach (var item in _idToOrder)
            {
                if (IsOurOrder(item.Value.ClientId))
                    orders[item.Key] = item.Value;
            }

            return new CryptoOrderCollectionReadonly(orders);
        }

        /// <summary>
        /// Returns all orders (ignore prefix for client id)
        /// </summary>
        public CryptoOrderCollectionReadonly GetAllOrders()
        {
            var orders = new Dictionary<string, CryptoOrder>(_idToOrder.Count);
            foreach (var item in _idToOrder)
                orders[item.Key] = item.Value;

            return new CryptoOrderCollectionReadonly(orders);
        }

        /// <summary>
        /// Find active order by provided unique id
        /// </summary>
        public CryptoOrder? FindActiveOrder(string id)
        {
            return _idToOrder.TryGetValue(id, out var order) && IsActive(order)
                ? order
                : null;
        }

        /// <summary>
        /// Find order by provided unique id
        /// </summary>
        public CryptoOrder? FindOrder(string id)
        {
            return _idToOrder.GetValueOrDefault(id);
        }

        /// <summary>
        /// Find active order by provided client id
        /// </summary>
        public CryptoOrder FindActiveOrderByClientId(string clientId)
        {
            foreach (var item in _idToOrder)
            {
                if (IsActive(item.Value) && item.Value.ClientId == clientId)
                    return item.Value;
            }

            return null!;
        }

        /// <summary>
        /// Find order by provided client id
        /// </summary>
        public CryptoOrder FindOrderByClientId(string clientId)
        {
            foreach (var item in _idToOrder)
            {
                if (item.Value.ClientId == clientId)
                    return item.Value;
            }

            return null!;
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
        public bool IsOurOrder(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(ClientIdPrefixString))
                return true;

            // if prefix is set, also client id has to be set to compare prefixes
            if (string.IsNullOrWhiteSpace(clientId))
                return false;

            return clientId.StartsWith($"{ClientIdPrefixString}0", StringComparison.Ordinal);
        }

        /// <summary>
        /// Track selected order (use immediately after placing an order via REST call)
        /// </summary>
        public void TrackOrder(CryptoOrder order)
        {
            OnOrderUpdated(order);
        }

        /// <summary>
        /// Clean internal orders cache, remove canceled orders
        /// </summary>
        public void RemoveCanceled()
        {
            foreach (var order in _idToOrder)
            {
                if (order.Value.OrderStatus == CryptoOrderStatus.Canceled ||
                    order.Value.OrderStatus == CryptoOrderStatus.Undefined)
                {
                    _idToOrder.TryRemove(order.Key, out CryptoOrder _);
                }
            }
        }


        private void Subscribe()
        {
            _source.OrdersInitialStream.Subscribe(OnOrdersUpdated);
            _source.OrderCreatedStream.Subscribe(OnOrderCreated);
            _source.OrderUpdatedStream.Subscribe(OnOrderUpdated);
        }

        private void OnOrdersUpdated(CryptoOrder[]? orders)
        {
            if (orders == null)
                return;

            foreach (var order in orders)
            {
                OnOrderUpdated(order);
            }
        }

        private void OnOrderCreated(CryptoOrder? order)
        {
            if (order == null)
                return;

            HandleOrderUpdated(order);
        }

        private void OnOrderUpdated(CryptoOrder? order)
        {
            if (order == null)
                return;

            if (order.OrderStatus == CryptoOrderStatus.Undefined)
            {
                _source.Logger.LogDebug("[ORDERS] Received order with weird status ({clientId} - {price}/{amount})", order.ClientId, order.PriceGrouped, order.AmountGrouped);
            }

            HandleOrderUpdated(order);
        }

        private void HandleOrderUpdated(CryptoOrder order)
        {
            if (IsFilteredOut(order))
            {
                // order for different pair, etc. Ignore.
                return;
            }

            _idToOrder[order.Id] = order;

            if (order.OrderStatus == CryptoOrderStatus.Executed || order.OrderStatus == CryptoOrderStatus.PartiallyFilled)
            {
                if (order.Side == CryptoOrderSide.Ask)
                    LastExecutedSellOrder = order;
                else
                    LastExecutedBuyOrder = order;
            }

            _orderChanged.OnNext(order);

            if (IsOurOrder(order))
                _ourOrderChanged.OnNext(order);
        }

        private bool IsFilteredOut(CryptoOrder? order)
        {
            if (order == null)
                return true;

            // filter out by selected pair
            if (!string.IsNullOrWhiteSpace(TargetPair) && !TargetPair.Equals(order.PairClean))
                return true;

            return false;
        }

        private static bool IsActive(CryptoOrder order)
        {
            return order.OrderStatus != CryptoOrderStatus.Canceled &&
                   order.OrderStatus != CryptoOrderStatus.Executed &&
                   order.OrderStatus != CryptoOrderStatus.Undefined;
        }
    }
}
