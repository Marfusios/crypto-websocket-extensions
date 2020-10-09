using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Structures
{
    [DebuggerDisplay("OrderBookLeaf [{Data.Pair}] {Data.Id} {Data.Amount} @ {Data.Price}")]
    internal class OrderBookLeaf
    {
        public OrderBookLeaf(OrderBookLevel data)
        {
            Data = data;
        }

        public OrderBookLeaf Previous { get; set; }
        public OrderBookLeaf Next { get; set; }
        public OrderBookLevel Data { get; set; }
    }
}
