using System.Collections.Generic;
using Crypto.Websocket.Extensions.Core.OrderBooks.Structures;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Order book levels together, indexed by id
    /// </summary>
    internal class OrderBookLevelsById : Dictionary<string, OrderBookLevel>
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

    /// <summary>
    /// Order book leafs together, indexed by id
    /// </summary>
    internal class OrderBookLeafsById : Dictionary<string, OrderBookLeaf>
    {
        /// <inheritdoc />
        public OrderBookLeafsById()
        {
        }

        /// <inheritdoc />
        public OrderBookLeafsById(int capacity) : base(capacity)
        {
        }
    }
}
