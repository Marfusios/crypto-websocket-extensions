using System;
using Crypto.Websocket.Extensions.Core.Orders.Models;

namespace Crypto.Websocket.Extensions.Core.Orders.Sources
{
    /// <summary>
    /// Source that provides current orders info
    /// </summary>
    public interface IOrderSource
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Stream snapshot of currently active orders
        /// </summary>
        IObservable<CryptoOrder[]> OrdersInitialStream { get; }

        /// <summary>
        /// Stream info about new active order
        /// </summary>
        IObservable<CryptoOrder> OrderCreatedStream { get; }

        /// <summary>
        /// Stream on every status change of the order
        /// </summary>
        IObservable<CryptoOrder> OrderUpdatedStream { get; }

        /// <summary>
        /// Set collection of existing orders (to correctly handle orders state)
        /// </summary>
        void SetExistingOrders(CryptoOrderCollection orders);
    }
}