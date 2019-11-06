namespace Crypto.Websocket.Extensions.Core.Orders.Models
{
    /// <summary>
    /// Current order status
    /// </summary>
    public enum CryptoOrderStatus
    {
        Undefined,
        Active,
        Executed,
        PartiallyFilled,
        Canceled
    }
}
