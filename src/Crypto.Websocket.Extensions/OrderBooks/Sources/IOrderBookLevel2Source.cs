using System;
using Crypto.Websocket.Extensions.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <summary>
    /// Order book source that provides level 2 data
    /// </summary>
    public interface IOrderBookLevel2Source
    {
        /// <summary>
        /// Streams initial snapshot of the order book
        /// </summary>
        IObservable<OrderBookLevel[]> OrderBookSnapshotStream { get; }

        /// <summary>
        /// Streams every update to the order book
        /// </summary>
        IObservable<OrderBookLevelBulk> OrderBookStream { get; }
    }
}
