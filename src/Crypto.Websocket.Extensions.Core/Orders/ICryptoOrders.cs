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
        IObservable<CryptoOrder> OrderChangedStream { get; }

        /// <summary>
        /// Order was changed stream (only ours, based on client id prefix)
        /// </summary>
        IObservable<CryptoOrder> OurOrderChangedStream { get; }

        /// <summary>
        /// Selected client id prefix
        /// </summary>
        long? ClientIdPrefix { get; }

        /// <summary>
        /// Selected client id prefix as string
        /// </summary>
        string ClientIdPrefixString { get; }

        /// <summary>
        /// Last executed (or partially filled) buy order
        /// </summary>
        CryptoOrder LastExecutedBuyOrder { get; }

        /// <summary>
        /// Last executed (or partially filled) sell order
        /// </summary>
        CryptoOrder LastExecutedSellOrder { get; }

        /// <summary>
        /// Generate a new client id (with prefix)
        /// </summary>
        long GenerateClientId();

        /// <summary>
        /// Returns only our active orders (based on client id prefix)
        /// </summary>
        CryptoOrderCollectionReadonly GetOurActiveOrders();

        /// <summary>
        /// Returns only our orders (based on client id prefix)
        /// </summary>
        CryptoOrderCollectionReadonly GetOurOrders();

        /// <summary>
        /// Returns all orders
        /// </summary>
        CryptoOrderCollectionReadonly GetOrders();

        /// <summary>
        /// Find active order by provided unique id
        /// </summary>
        CryptoOrder FindActiveOrder(string id);

        /// <summary>
        /// Find order by provided unique id
        /// </summary>
        CryptoOrder FindOrder(string id);

        /// <summary>
        /// Find active order by provided client id
        /// </summary>
        CryptoOrder FindActiveOrderByClientId(string clientId);

        /// <summary>
        /// Find order by provided client id
        /// </summary>
        CryptoOrder FindOrderByClientId(string clientId);

        /// <summary>
        /// Returns true if client id matches prefix
        /// </summary>
        bool IsOurOrder(CryptoOrder order);

        /// <summary>
        /// Returns true if client id matches prefix
        /// </summary>
        bool IsOurOrder(string clientId);
    }
}
