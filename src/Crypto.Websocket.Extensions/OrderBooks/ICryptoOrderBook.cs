using System;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.OrderBooks
{
    /// <summary>
    /// Cryptocurrency order book.
    /// Process order book data from one source (exchange) and one target pair. 
    /// </summary>
    public interface ICryptoOrderBook
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Target pair for this order book data
        /// </summary>
        string TargetPair { get; }

        /// <summary>
        /// Originally provided target pair for this order book data
        /// </summary>
        string TargetPairOriginal { get; }

        /// <summary>
        /// Time interval for auto snapshot reloading.
        /// Default 1 min. 
        /// </summary>
        TimeSpan SnapshotReloadTimeout { get; set; }

        /// <summary>
        /// Whenever auto snapshot reloading feature is enabled.
        /// Enabled by default
        /// </summary>
        bool SnapshotReloadEnabled { get; set; }

        /// <summary>
        /// Streams data when top level bid or ask price was updated
        /// </summary>
        IObservable<OrderBookChangeInfo> BidAskUpdatedStream { get; }

        /// <summary>
        /// Streams data on every order book change (price or amount at any level)
        /// </summary>
        IObservable<OrderBookChangeInfo> OrderBookUpdatedStream { get; }

        /// <summary>
        /// Current bid side of the order book (ordered from higher to lower price)
        /// </summary>
        OrderBookLevel[] BidLevels { get; }

        /// <summary>
        /// Current ask side of the order book (ordered from lower to higher price)
        /// </summary>
        OrderBookLevel[] AskLevels { get; }

        /// <summary>
        /// All current levels together
        /// </summary>
        OrderBookLevel[] Levels { get; }

        /// <summary>
        /// Current top level bid price
        /// </summary>
        double BidPrice { get; }

        /// <summary>
        /// Current top level ask price
        /// </summary>
        double AskPrice { get; }

        /// <summary>
        /// Current mid price
        /// </summary>
        double MidPrice { get; }

        /// <summary>
        /// Find bid level by provided price (returns null in case of not found)
        /// </summary>
        OrderBookLevel FindBidLevelByPrice(double price);

        /// <summary>
        /// Find ask level by provided price (returns null in case of not found)
        /// </summary>
        OrderBookLevel FindAskLevelByPrice(double price);

        /// <summary>
        /// Find bid level by provided identification (returns null in case of not found)
        /// </summary>
        OrderBookLevel FindBidLevelById(string id);

        /// <summary>
        /// Find ask level by provided identification (returns null in case of not found)
        /// </summary>
        OrderBookLevel FindAskLevelById(string id);

        /// <summary>
        /// Find level by provided identification (returns null in case of not found).
        /// You need to specify side.
        /// </summary>
        OrderBookLevel FindLevelById(string id, CryptoSide side);
    }
}