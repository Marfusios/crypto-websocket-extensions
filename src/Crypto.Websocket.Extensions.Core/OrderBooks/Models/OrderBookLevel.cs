using System;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Utils;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// One level of the order book
    /// </summary>
    [DebuggerDisplay("OrderBookLevel [{Pair}] {Id} {Amount} @ {Price}")]
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

            Amount = Abs(Amount);
            Count = Abs(Count);
        }

        /// <summary>
        /// Unique identification of this level or order id
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
        /// Number of orders at that price level or order
        /// </summary>
        public int? Count { get; internal set; }

        /// <summary>
        /// Total amount available at that price level or order. 
        /// </summary>
        public double? Amount { get; internal set; }

        /// <summary>
        /// Pair to which this level or order belongs
        /// </summary>
        public string Pair { get; internal set; }

        /// <summary>
        /// Ordering index to determine position in the queue
        /// </summary>
        public int Ordering { get; internal set; }

        /// <summary>
        /// How many times price was updated for this order book level/order (makes sense only for L3)
        /// </summary>
        public int PriceUpdatedCount { get; internal set; }

        /// <summary>
        /// How many times amount was updated for this order book level/order (makes sense for both L3 and L2)
        /// </summary>
        public int AmountUpdatedCount { get; internal set; }

        /// <summary>
        /// Difference between previous level amount and current one, negative means that current level amount is smaller than it was before (makes sense for both L3 and L2)
        /// </summary>
        public double AmountDifference { get; internal set; }

        /// <summary>
        /// Difference between first level amount and current one, negative means that current level amount is smaller than it was at the beginning (makes sense for both L3 and L2)
        /// </summary>
        public double AmountDifferenceAggregated { get; internal set; }

        /// <summary>
        /// Difference between previous level order count and current one, negative means that there are fewer orders on that level (makes sense only for L2)
        /// </summary>
        public int CountDifference { get; internal set; }

        /// <summary>
        /// Difference between first level order count and current one, negative means that there are fewer orders on that level than at the beginning (makes sense only for L2)
        /// </summary>
        public int CountDifferenceAggregated { get; internal set; }

        /// <summary>
        /// Level index (position) in the order book.
        /// Beware not updated after first set! 
        /// </summary>
        public int? Index { get; internal set; }

        /// <summary>
        /// Create a new clone
        /// </summary>
        public OrderBookLevel Clone()
        {
            return new(
                Id,
                Side,
                Price,
                Amount,
                Count,
                Pair
                )
            {
                Ordering = Ordering,
                PriceUpdatedCount = PriceUpdatedCount,
                AmountUpdatedCount = AmountUpdatedCount,
                AmountDifference = AmountDifference,
                CountDifference = CountDifference,
                AmountDifferenceAggregated = AmountDifferenceAggregated,
                CountDifferenceAggregated = CountDifferenceAggregated
            };
        }

        static double? Abs(double? value)
        {
            if (value.HasValue)
                return Math.Abs(value.Value);
            return null;
        }

        static int? Abs(int? value)
        {
            if (value.HasValue)
                return Math.Abs(value.Value);
            return null;
        }
    }
}
