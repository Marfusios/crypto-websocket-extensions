namespace Crypto.Websocket.Extensions.Core.Orders.Models
{
    /// <summary>
    /// Order type - limit, market, etc.
    /// </summary>
    public enum CryptoOrderType
    {
#pragma warning disable 1591
        Undefined,
        Limit,
        Market,
        Stop,
        TrailingStop,
        Fok,
        StopLimit,
        TakeProfitLimit,
        TakeProfitMarket
#pragma warning restore 1591
    }
}
