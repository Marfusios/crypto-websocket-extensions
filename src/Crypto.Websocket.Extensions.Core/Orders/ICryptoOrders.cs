using System;
using Crypto.Websocket.Extensions.Core.Orders.Models;

namespace Crypto.Websocket.Extensions.Core.Orders
{
    /// <summary>
    /// Orders manager
    /// </summary>
    public interface ICryptoOrders
    {
        /// <summary>
        /// Order was changed stream
        /// </summary>
        IObservable<Models.CryptoOrder> OrderChangedStream { get; }

        /// <summary>
        /// Order was changed stream (only ours, based on client id prefix)
        /// </summary>
        IObservable<Models.CryptoOrder> OurOrderChangedStream { get; }

        /// <summary>
        /// Selected client id prefix
        /// </summary>
        long? ClientIdPrefix { get; }

        /// <summary>
        /// Selected client id prefix as string
        /// </summary>
        string ClientIdPrefixString { get; }

        /// <summary>
        /// Client id exponent when prefix is selected.
        /// For example:
        /// prefix = 333
        /// exponent = 1000000
        /// generated client id = 333000001
        /// </summary>
        long ClientIdPrefixExponent { get; set; }

        /// <summary>
        /// Target pair for this orders data (other orders will be filtered out)
        /// </summary>
        string TargetPair { get; }

        /// <summary>
        /// Originally provided target pair for this orders data
        /// </summary>
        string TargetPairOriginal { get; }

        /// <summary>
        /// Last executed (or partially filled) buy order
        /// </summary>
        Models.CryptoOrder LastExecutedBuyOrder { get; }

        /// <summary>
        /// Last executed (or partially filled) sell order
        /// </summary>
        Models.CryptoOrder LastExecutedSellOrder { get; }

        /// <summary>
        /// Generate a new client id (with prefix)
        /// </summary>
        long GenerateClientId();

        /// <summary>
        /// Returns only our active orders (based on client id prefix)
        /// </summary>
        CryptoOrderCollectionReadonly GetActiveOrders();

        /// <summary>
        /// Returns only our orders (based on client id prefix)
        /// </summary>
        CryptoOrderCollectionReadonly GetOrders();

        /// <summary>
        /// Returns all orders (ignore prefix for client id)
        /// </summary>
        CryptoOrderCollectionReadonly GetAllOrders();

        /// <summary>
        /// Find active order by provided unique id
        /// </summary>
        Models.CryptoOrder FindActiveOrder(string id);

        /// <summary>
        /// Find order by provided unique id
        /// </summary>
        Models.CryptoOrder FindOrder(string id);

        /// <summary>
        /// Find active order by provided client id
        /// </summary>
        Models.CryptoOrder FindActiveOrderByClientId(string clientId);

        /// <summary>
        /// Find order by provided client id
        /// </summary>
        Models.CryptoOrder FindOrderByClientId(string clientId);

        /// <summary>
        /// Returns true if client id matches prefix
        /// </summary>
        bool IsOurOrder(CryptoOrder order);

        /// <summary>
        /// Returns true if client id matches prefix
        /// </summary>
        bool IsOurOrder(string clientId);

        /// <summary>
        /// Track selected order (use immediately after placing an order via REST call)
        /// </summary>
        void TrackOrder(CryptoOrder order);

        /// <summary>
        /// Clean internal orders cache, remove canceled orders
        /// </summary>
        void RemoveCanceled();
    }
}