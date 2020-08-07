using System.Collections.Generic;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Order book price to order index
    /// </summary>
    public class OrderBookLevelsOrderPerPrice : Dictionary<double, int>
    {
        /// <inheritdoc />
        public OrderBookLevelsOrderPerPrice()
        {
        }

        /// <inheritdoc />
        public OrderBookLevelsOrderPerPrice(int capacity) : base(capacity)
        {
        }
    }
}
