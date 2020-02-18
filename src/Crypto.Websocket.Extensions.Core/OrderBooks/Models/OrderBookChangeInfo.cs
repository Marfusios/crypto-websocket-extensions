using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Info about changed order book
    /// </summary>
    public class OrderBookChangeInfo : CryptoChangeInfo, IOrderBookChangeInfo
    {
        /// <inheritdoc />
        public OrderBookChangeInfo(string pair, string pairOriginal,
            CryptoQuotes quotes, OrderBookLevel[] levels, OrderBookLevelBulk[] sources)
        {
            Pair = pair;
            PairOriginal = pairOriginal;
            Quotes = quotes;
            Sources = sources;
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
    }
}