using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Info about changed order book
    /// </summary>
    public class OrderBookChangeInfo : IOrderBookChangeInfo
    {
        /// <inheritdoc />
        public OrderBookChangeInfo(string exchangeName, string pair, string pairOriginal,
            CryptoQuotes quotes, OrderBookLevel[] levels)
        {
            ExchangeName = exchangeName;
            Pair = pair;
            PairOriginal = pairOriginal;
            Quotes = quotes;
            Levels = levels ?? new OrderBookLevel[0];
        }

        /// <summary>
        /// Origin exchange name
        /// </summary>
        public string ExchangeName { get; }

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
    }
}
