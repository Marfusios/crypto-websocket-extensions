using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Info about changed order book
    /// </summary>
    public interface IOrderBookChangeInfo : ICryptoChangeInfo
    {
        /// <summary>
        /// Target pair for this quotes
        /// </summary>
        string Pair { get; }

        /// <summary>
        /// Unmodified target pair for this quotes
        /// </summary>
        string PairOriginal { get; }

        /// <summary>
        /// Current quotes
        /// </summary>
        ICryptoQuotes Quotes { get; }

        /// <summary>
        /// Order book levels that caused the change.
        /// Streamed only when debug mode is enabled. 
        /// </summary>
        OrderBookLevel[] Levels { get; }

        /// <summary>
        /// Source bulks that caused this update (all levels)
        /// </summary>
        OrderBookLevelBulk[] Sources { get; }

        /// <summary>
        /// Whenever this order book change update comes from snapshot or diffs
        /// </summary>
        bool IsSnapshot { get; }
    }
}