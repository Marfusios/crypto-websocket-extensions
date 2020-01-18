namespace Crypto.Websocket.Extensions.Core.Orders.Models
{
    /// <summary>
    /// Current order status
    /// </summary>
    public enum CryptoOrderStatus
    {
        Undefined,
        New,
        Active,
        Executed,
        PartiallyFilled,
        Canceled,
        Pending
    }
}
