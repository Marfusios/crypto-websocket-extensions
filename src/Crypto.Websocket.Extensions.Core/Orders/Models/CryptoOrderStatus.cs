namespace Crypto.Websocket.Extensions.Core.Orders.Models
{
    /// <summary>
    /// Current order status
    /// </summary>
    public enum CryptoOrderStatus
    {
#pragma warning disable 1591
        Undefined,
        New,
        Active,
        Executed,
        PartiallyFilled,
        Canceled
#pragma warning restore 1591
    }
}
