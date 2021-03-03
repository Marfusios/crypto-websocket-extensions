using System;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// Cryptocurrency order book.
    /// Process order book data from one source (exchange) and one target pair. 
    /// </summary>
    public interface ICryptoOrderBook : IDisposable
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
        /// Order book type, which precision it supports
        /// </summary>
        CryptoOrderBookType TargetType { get; }

        /// <summary>
        /// Time interval for auto snapshot reloading.
        /// Default 1 min. 
        /// </summary>
        TimeSpan SnapshotReloadTimeout { get; set; }

        /// <summary>
        /// Whenever auto snapshot reloading feature is enabled.
        /// Disabled by default
        /// </summary>
        bool SnapshotReloadEnabled { get; set; }

        /// <summary>
        /// Time interval for validity checking.
        /// It forces snapshot reloading whenever invalid state. 
        /// Default 5 sec. 
        /// </summary>
        TimeSpan ValidityCheckTimeout { get; set; }

        /// <summary>
        /// How many times it should check validity before processing snapshot reload.
        /// Default 6 times (which is 6 * 5sec = 30sec).
        /// </summary>
        int ValidityCheckLimit { get; set; }

        /// <summary>
        /// Whenever validity checking feature is enabled.
        /// It forces snapshot reloading whenever invalid state. 
        /// Enabled by default
        /// </summary>
        bool ValidityCheckEnabled { get; set; }

        /// <summary>
        /// Provide more info (on every change) whenever enabled. 
        /// Disabled by default
        /// </summary>
        bool DebugEnabled { get; set; }

        /// <summary>
        /// Logs more info (state, performance) whenever enabled. 
        /// Disabled by default
        /// </summary>
        bool DebugLogEnabled { get; set; }

        /// <summary>
        /// Whenever snapshot was already handled
        /// </summary>
        bool IsSnapshotLoaded { get; }

        /// <summary>
        /// All diffs/deltas that come before snapshot will be ignored (default: true)
        /// </summary>
        bool IgnoreDiffsBeforeSnapshot { get; set; }

        /// <summary>
        /// Compute index (position) per every updated level, performance is slightly reduced (default: false) 
        /// </summary>
        bool IsIndexComputationEnabled { get; set; }

        /// <summary>
        /// Streams data when top level bid or ask price was updated
        /// </summary>
        IObservable<IOrderBookChangeInfo> BidAskUpdatedStream { get; }

        /// <summary>
        /// Streams data when top level bid or ask price or amount was updated
        /// </summary>
        IObservable<IOrderBookChangeInfo> TopLevelUpdatedStream { get; }

        /// <summary>
        /// Streams data on every order book change (price or amount at any level)
        /// </summary>
        IObservable<IOrderBookChangeInfo> OrderBookUpdatedStream { get; }

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
        /// Current top level bid amount
        /// </summary>
        double BidAmount { get; }

        /// <summary>
        /// Current top level ask amount
        /// </summary>
        double AskAmount { get; }

        /// <summary>
        /// Find bid level for provided price (returns null in case of missing)
        /// </summary>
        OrderBookLevel FindBidLevelByPrice(double price);

        /// <summary>
        /// Find all bid levels for provided price (returns empty when not found)
        /// </summary>
        OrderBookLevel[] FindBidLevelsByPrice(double price);

        /// <summary>
        /// Find ask level for provided price (returns null in case of missing)
        /// </summary>
        OrderBookLevel FindAskLevelByPrice(double price);

        /// <summary>
        /// Find all ask levels for provided price (returns empty when not found)
        /// </summary>
        OrderBookLevel[] FindAskLevelsByPrice(double price);

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
        OrderBookLevel FindLevelById(string id, CryptoOrderSide side);

        /// <summary>
        /// Returns true if order book is in valid state
        /// </summary>
        bool IsValid();
    }
}