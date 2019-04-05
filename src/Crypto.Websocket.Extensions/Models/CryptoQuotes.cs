namespace Crypto.Websocket.Extensions.Models
{
    /// <summary>
    /// Price quotes
    /// </summary>
    public class CryptoQuotes
    {
        /// <inheritdoc />
        public CryptoQuotes(double bid, double ask, string exchangeName, string pair)
        {
            Bid = bid;
            Ask = ask;
            ExchangeName = exchangeName;
            Pair = pair;
            Mid = (bid + ask) / 2;
        }

        /// <summary>
        /// Top level bid price
        /// </summary>
        public double Bid { get; }

        /// <summary>
        /// Top level ask price
        /// </summary>
        public double Ask { get; }

        /// <summary>
        /// Current mid price
        /// </summary>
        public double Mid { get; }

        /// <summary>
        /// Origin exchange name
        /// </summary>
        public string ExchangeName { get; }

        /// <summary>
        /// Target pair for this quotes
        /// </summary>
        public string Pair { get; }
    }
}
