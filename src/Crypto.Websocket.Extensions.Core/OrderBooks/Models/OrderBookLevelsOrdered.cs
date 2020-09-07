using System.Collections.Generic;
using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Order book levels ordered by price
    /// </summary>
    public class OrderBookLevelsOrdered : SortedDictionary<double, OrderBookPriceLevels>
    {
        /// <summary>
        /// Order book levels ordered by price, specify side
        /// </summary>
        public OrderBookLevelsOrdered(CryptoOrderSide side) :
            base(side == CryptoOrderSide.Bid ? new DescendingComparer() : null)
        {
            
        }

        private class DescendingComparer : IComparer<double> {
            public int Compare(double x, double y) {
                return y.CompareTo(x);
            }
        }
    }
}
