using System.Diagnostics;

namespace Crypto.Websocket.Extensions.OrderBooks.Models
{
    /// <summary>
    /// Groups together order book levels that are coming from server
    /// </summary>
    [DebuggerDisplay("OrderBookLevelBulk [{Action}] count: {Levels.Length}")]
    public class OrderBookLevelBulk
    {
        /// <inheritdoc />
        public OrderBookLevelBulk(OrderBookAction action, OrderBookLevel[] levels)
        {
            Action = action;
            Levels = levels ?? new OrderBookLevel[0];
        }

        /// <summary>
        /// Action of this bulk
        /// </summary>
        public OrderBookAction Action { get; }

        /// <summary>
        /// Order book levels for this bulk
        /// </summary>
        public OrderBookLevel[] Levels { get; }
    }
}
