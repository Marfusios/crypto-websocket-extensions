using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Info about changed order book
    /// </summary>
    [DebuggerDisplay("OrderBookChangeInfo [{PairOriginal}] {Quotes} sources: {Sources.Length}")]
    public class OrderBookChangeInfo : CryptoChangeInfo, IOrderBookChangeInfo
    {
        /// <inheritdoc />
        public OrderBookChangeInfo(string pair, string pairOriginal,
            CryptoQuotes quotes, OrderBookLevel[] levels, OrderBookLevelBulk[] sources, 
            bool isSnapshot)
        {
            Pair = pair;
            PairOriginal = pairOriginal;
            Quotes = quotes;
            Sources = sources;
            IsSnapshot = isSnapshot;
            Levels = levels ?? new OrderBookLevel[0];
        }

        /// <summary>
        /// Target pair for this quotes
        /// </summary>
        public string Pair { get; }

        /// <summary>
        /// Unmodified target pair for this quotes
        /// </summary>
        public string PairOriginal { get; }

        /// <summary>
        /// Current quotes
        /// </summary>
        public ICryptoQuotes Quotes { get; }

        /// <summary>
        /// Order book levels that caused the change.
        /// Streamed only when debug mode is enabled. 
        /// </summary>
        public OrderBookLevel[] Levels { get; }

        /// <summary>
        /// Source bulks that caused this update (all levels)
        /// </summary>
        public OrderBookLevelBulk[] Sources { get; }

        /// <summary>
        /// Whenever this order book change update comes from snapshot or diffs
        /// </summary>
        public bool IsSnapshot { get; }
    }
}
