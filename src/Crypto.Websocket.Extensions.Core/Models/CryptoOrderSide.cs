namespace Crypto.Websocket.Extensions.Core.Models
{
    /// <summary>
    /// Order side - bid or ask
    /// </summary>
    public enum CryptoOrderSide
    {
        /// <summary>
        /// Unknown side
        /// </summary>
        Undefined = 0,

        /// <summary>
        /// Buy side
        /// </summary>
        Bid = 1,

        /// <summary>
        /// Sell side
        /// </summary>
        Ask = 2
    }
}