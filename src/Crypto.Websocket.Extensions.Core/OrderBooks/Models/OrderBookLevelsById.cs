using System.Collections.Generic;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Order book levels together, indexed by id
    /// </summary>
    public class OrderBookLevelsById : Dictionary<string, OrderBookLevel>
    {
        /// <inheritdoc />
        public OrderBookLevelsById()
        {
        }

        /// <inheritdoc />
        public OrderBookLevelsById(int capacity) : base(capacity)
        {
        }
    }
}
