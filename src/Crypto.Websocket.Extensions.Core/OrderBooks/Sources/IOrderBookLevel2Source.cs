using System;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Sources
{
    /// <summary>
    /// Order book source that provides level 2 data
    /// </summary>
    public interface IOrderBookLevel2Source : IDisposable
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Enable or disable snapshot loading (used by auto snapshot reload feature on OrderBook). 
        /// Disabled by default. 
        /// </summary>
        bool LoadSnapshotEnabled { get; set; }

        /// <summary>
        /// Whenever messages should be buffered before processing.
        /// Use property `BufferInterval` to configure buffering interval.
        /// Enabled by default. 
        /// </summary>
        bool BufferEnabled { get; set; }

        /// <summary>
        /// Time interval for buffering received order book data updates.
        /// Higher it for data intensive sources (Bitmex, etc.)
        /// Lower - more realtime data, high CPU load.
        /// Higher - less realtime data, less CPU intensive. 
        /// Default: 10 ms
        /// </summary>
        TimeSpan BufferInterval { get; set; }

        /// <summary>
        /// Streams initial snapshot of the order book
        /// </summary>
        IObservable<OrderBookLevel[]> OrderBookSnapshotStream { get; }

        /// <summary>
        /// Streams every update to the order book
        /// </summary>
        IObservable<OrderBookLevelBulk[]> OrderBookStream { get; }

        /// <summary>
        /// Request a new order book snapshot, will be streamed via 'OrderBookSnapshotStream'.
        /// Method doesn't throw exception, just logs it
        /// </summary>
        /// <param name="pair">Target pair</param>
        /// <param name="count">Max level count</param>
        Task LoadSnapshot(string pair, int count = 1000);

        /// <summary>
        /// Returns true if order book is in valid state
        /// </summary>
        bool IsValid();
    }
}
