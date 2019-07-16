namespace Crypto.Websocket.Extensions.Core.Models
{
    /// <summary>
    /// Price quotes
    /// </summary>
    public interface ICryptoQuotes
    {
        /// <summary>
        /// Top level bid price
        /// </summary>
        double Bid { get; }

        /// <summary>
        /// Top level ask price
        /// </summary>
        double Ask { get; }

        /// <summary>
        /// Current mid price
        /// </summary>
        double Mid { get; }

        /// <summary>
        /// Top level bid amount
        /// </summary>
        double BidAmount { get; }

        /// <summary>
        /// Top level ask amount
        /// </summary>
        double AskAmount { get; }
    }
}