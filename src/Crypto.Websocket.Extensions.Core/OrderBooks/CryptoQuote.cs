using System.Diagnostics;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// A price and amount.
    /// </summary>
    [DebuggerDisplay("Price: {Price}, Amount: {Amount}")]
    public class CryptoQuote
    {
        /// <summary>
        /// Creates a new quote.
        /// </summary>
        /// <param name="price">The price.</param>
        /// <param name="amount">The amount.</param>
        public CryptoQuote(double price, double amount)
        {
            Price = price;
            Amount = amount;
        }

        /// <summary>
        /// The price.
        /// </summary>
        public double Price { get; set; }

        /// <summary>
        /// The amount.
        /// </summary>
        public double Amount { get; set; }

        internal bool IsValid => Price != 0 && Amount != 0;
    }
}
