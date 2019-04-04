namespace Crypto.Websocket.Extensions.OrderBooks.Models
{
    /// <summary>
    /// Action type of the order book data
    /// </summary>
    public enum OrderBookAction
    {
        /// <summary>
        /// Unknown action
        /// </summary>
        Undefined,

        /// <summary>
        /// Insert a new order book level
        /// </summary>
        Insert,

        /// <summary>
        /// Update order book level
        /// </summary>
        Update,

        /// <summary>
        /// Delete order book level
        /// </summary>
        Delete,
    }
}
