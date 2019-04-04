using System;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Models;

namespace Crypto.Websocket.Extensions.OrderBooks.Models
{
    /// <summary>
    /// One level of the order book
    /// </summary>
    [DebuggerDisplay("OrderBookLevel [{Pair}] {Amount} @ {Price}")]
    public class OrderBookLevel
    {
        /// <inheritdoc />
        public OrderBookLevel(string id, CryptoSide side, double? price, double? amount, int? count, string pair)
        {
            Id = id;
            Side = side;
            Price = price;
            Count = count;
            Amount = amount;
            Pair = pair;

            Price = Abs(Price);
            Amount = Abs(Amount);
            Count = Abs(Count);
        }

        /// <summary>
        /// Unique identification of this level
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// Side of this order book level
        /// </summary>
        public CryptoSide Side { get; private set; }

        /// <summary>
        /// Price level
        /// </summary>
        public double? Price { get; private set; }

        /// <summary>
        /// Number of orders at that price level
        /// </summary>
        public int? Count { get; private set; }

        /// <summary>
        /// Total amount available at that price level. 
        /// </summary>
        public double? Amount { get; private set; }

        /// <summary>
        /// Pair to which this level belongs
        /// </summary>
        public string Pair { get; private set; }


        private double? Abs(double? value)
        {
            if (value.HasValue)
                return Math.Abs(value.Value);
            return null;
        }

        private int? Abs(int? value)
        {
            if (value.HasValue)
                return Math.Abs(value.Value);
            return null;
        }
    }
}
