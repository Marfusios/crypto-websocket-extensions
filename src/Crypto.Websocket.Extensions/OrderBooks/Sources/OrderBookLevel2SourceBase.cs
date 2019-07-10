using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public abstract class OrderBookLevel2SourceBase : IOrderBookLevel2Source
    {
        /// <summary>
        /// Use this subject to stream order book snapshot data
        /// </summary>
        protected readonly Subject<OrderBookLevel[]> OrderBookSnapshotSubject = new Subject<OrderBookLevel[]>();

        /// <summary>
        /// Use this subject to stream order book data (level difference)
        /// </summary>
        protected readonly Subject<OrderBookLevelBulk> OrderBookSubject = new Subject<OrderBookLevelBulk>();


        /// <inheritdoc />
        public abstract string ExchangeName { get; }

        /// <inheritdoc />
        public bool LoadSnapshotEnabled { get; protected set; } = true;

        /// <inheritdoc />
        public IObservable<OrderBookLevel[]> OrderBookSnapshotStream => OrderBookSnapshotSubject.AsObservable();

        /// <inheritdoc />
        public IObservable<OrderBookLevelBulk> OrderBookStream => OrderBookSubject.AsObservable();

        /// <inheritdoc />
        public abstract Task LoadSnapshot(string pair, int count = 1000);
    }
}
