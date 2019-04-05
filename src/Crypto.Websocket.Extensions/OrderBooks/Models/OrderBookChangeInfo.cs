using Crypto.Websocket.Extensions.Models;

namespace Crypto.Websocket.Extensions.OrderBooks.Models
{
    /// <summary>
    /// Info about changed order book
    /// </summary>
    public class OrderBookChangeInfo
    {
        /// <inheritdoc />
        public OrderBookChangeInfo(string exchangeName, string pair, 
            CryptoQuotes quotes, OrderBookLevel[] levels)
        {
            ExchangeName = exchangeName;
            Pair = pair;
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
        /// Current quotes
        /// </summary>
        public CryptoQuotes Quotes { get; }

        /// <summary>
        /// Order book levels that caused the change
        /// </summary>
        public OrderBookLevel[] Levels { get; }
    }
}
