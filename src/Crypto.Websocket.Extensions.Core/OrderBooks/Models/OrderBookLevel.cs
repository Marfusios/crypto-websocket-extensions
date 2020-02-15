using System;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Utils;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// One level of the order book
    /// </summary>
    [DebuggerDisplay("OrderBookLevel [{Pair}] {Amount} @ {Price}")]
    public class OrderBookLevel
    {
        /// <summary>
        /// Level constructor
        /// </summary>
        public OrderBookLevel(string id, CryptoOrderSide side, double? price, double? amount, int? count, string pair)
        {
            Id = id;
            Side = side;
            Price = price;
            Count = count;
            Amount = amount;
            Pair = pair == null ? null : CryptoPairsHelper.Clean(pair);

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
        public CryptoOrderSide Side { get; private set; }

        /// <summary>
        /// Price level
        /// </summary>
        public double? Price { get; internal set; }

        /// <summary>
        /// Number of orders at that price level
        /// </summary>
        public int? Count { get; internal set; }

        /// <summary>
        /// Total amount available at that price level. 
        /// </summary>
        public double? Amount { get; internal set; }

        /// <summary>
        /// Pair to which this level belongs
        /// </summary>
        public string Pair { get; internal set; }

        /// <summary>
        /// Create a new clone
        /// </summary>
        public OrderBookLevel Clone()
        {
            return new OrderBookLevel(
                Id,
                Side,
                Price,
                Amount,
                Count,
                Pair
            );
        }

        private static double? Abs(double? value)
        {
            if (value.HasValue)
                return Math.Abs(value.Value);
            return null;
        }

        private static int? Abs(int? value)
        {
            if (value.HasValue)
                return Math.Abs(value.Value);
            return null;
        }
    }
}