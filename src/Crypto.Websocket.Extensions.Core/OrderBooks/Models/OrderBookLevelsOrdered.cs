using System.Collections.Generic;
using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Order book levels ordered
    /// </summary>
    public class OrderBookLevelsOrdered : SortedSet<OrderBookLevel>
    {
        /// <summary>
        /// Order book levels ordered, set side
        /// </summary>
        public OrderBookLevelsOrdered(CryptoOrderSide side) 
            : base(new OrderBookLevelComparer(side))
        {
        }

        private class OrderBookLevelComparer : IComparer<OrderBookLevel>
        {
            private readonly int _priceSide = 1;

            public OrderBookLevelComparer(CryptoOrderSide side)
            {
                if (side == CryptoOrderSide.Bid)
                    _priceSide = -1;
            }

            public int Compare(OrderBookLevel x, OrderBookLevel y)
            {
                var compared = (x?.Price ?? 0).CompareTo(y?.Price ?? 0);
                //if (compared == 0)
                //    return _priceSide;
                return compared * _priceSide;
            }
        }
    }
}
