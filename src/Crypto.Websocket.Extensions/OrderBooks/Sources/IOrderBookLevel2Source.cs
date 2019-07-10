using System;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <summary>
    /// Order book source that provides level 2 data
    /// </summary>
    public interface IOrderBookLevel2Source
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Streams initial snapshot of the order book
        /// </summary>
        IObservable<OrderBookLevel[]> OrderBookSnapshotStream { get; }

        /// <summary>
        /// Streams every update to the order book
        /// </summary>
        IObservable<OrderBookLevelBulk> OrderBookStream { get; }

        /// <summary>
        /// Request a new order book snapshot, will be streamed via 'OrderBookSnapshotStream'.
        /// Method doesn't throw exception, just logs it
        /// </summary>
        /// <param name="pair">Target pair</param>
        /// <param name="count">Max level count</param>
        Task LoadSnapshot(string pair, int count = 1000);
    }
}
