using System.Collections.Generic;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// A snapshot of bid and ask levels.
    /// </summary>
    [DebuggerDisplay("Bid: {Bid}/{BidAmount}, Ask: {Ask}/{AskAmount}, Bids: {Bids.Count}, Asks: {Asks.Count}")]
    public class L2Snapshot : CryptoQuotes
    {
        /// <summary>
        /// Creates a new snapshot.
        /// </summary>
        /// <param name="cryptoOrderBook">The source.</param>
        /// <param name="bids">The bids.</param>
        /// <param name="asks">The asks.</param>
        public L2Snapshot(ICryptoOrderBook cryptoOrderBook, IReadOnlyList<CryptoQuote> bids, IReadOnlyList<CryptoQuote> asks)
            : base(cryptoOrderBook.BidPrice, cryptoOrderBook.AskPrice, cryptoOrderBook.BidAmount, cryptoOrderBook.AskAmount)
        {
            Bids = bids;
            Asks = asks;
        }

        /// <summary>
        /// Updates the snapshot.
        /// </summary>
        /// <param name="bid">The bid price.</param>
        /// <param name="ask">The ask price.</param>
        /// <param name="bidAmount">The bid amount.</param>
        /// <param name="askAmount">The ask amount.</param>
        internal void Update(double bid, double ask, double bidAmount, double askAmount)
        {
            Bid = bid;
            Ask = ask;
            BidAmount = bidAmount;
            AskAmount = askAmount;
            Mid = (bid + ask) / 2;
        }

        /// <summary>
        /// Bid levels.
        /// </summary>
        public IReadOnlyList<CryptoQuote> Bids { get; }

        /// <summary>
        /// Ask levels.
        /// </summary>
        public IReadOnlyList<CryptoQuote> Asks { get; }
    }
}
