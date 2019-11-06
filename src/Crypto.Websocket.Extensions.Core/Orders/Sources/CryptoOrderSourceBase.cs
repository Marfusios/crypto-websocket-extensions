using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Crypto.Websocket.Extensions.Core.Orders.Models;

namespace Crypto.Websocket.Extensions.Core.Orders.Sources
{
    /// <summary>
    /// Source that provides current orders info 
    /// </summary>
    public abstract class CryptoOrderSourceBase : ICryptoOrderSource
    {
        protected readonly Subject<CryptoOrder[]> OrderSnapshotSubject = new Subject<CryptoOrder[]>();
        protected readonly Subject<CryptoOrder> OrderCreatedSubject = new Subject<CryptoOrder>();
        protected readonly Subject<CryptoOrder> OrderUpdatedSubject = new Subject<CryptoOrder>();

        /// <summary>
        /// Origin exchange name
        /// </summary>
        public abstract string ExchangeName { get; }

        /// <summary>
        /// Stream snapshot of currently active orders
        /// </summary>
        public virtual IObservable<CryptoOrder[]> OrdersInitialStream => OrderSnapshotSubject.AsObservable();

        /// <summary>
        /// Stream info about new active order
        /// </summary>
        public virtual IObservable<CryptoOrder> OrderCreatedStream => OrderCreatedSubject.AsObservable();

        /// <summary>
        /// Stream on every status change of the order
        /// </summary>
        public virtual IObservable<CryptoOrder> OrderUpdatedStream => OrderUpdatedSubject.AsObservable();
    }
}
