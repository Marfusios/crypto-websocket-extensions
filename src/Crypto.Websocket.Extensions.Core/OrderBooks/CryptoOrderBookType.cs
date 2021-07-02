namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// Order book type
    /// </summary>
    public enum CryptoOrderBookType
    {
        /// <summary>
        /// Unspecified
        /// </summary>
        Undefined,

        /// <summary>
        /// Only best bid/ask, quotes
        /// </summary>
        L1,

        /// <summary>
        /// Grouped by price
        /// </summary>
        L2,

        /// <summary>
        /// Raw, every single order
        /// </summary>
        L3,

        /// <summary>
        /// Everything
        /// </summary>
        All
    }
}
