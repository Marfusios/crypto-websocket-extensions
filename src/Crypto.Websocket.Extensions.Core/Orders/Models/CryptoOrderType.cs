namespace Crypto.Websocket.Extensions.Core.Orders.Models
{
    /// <summary>
    /// Order type - limit, market, etc.
    /// </summary>
    public enum CryptoOrderType
    {
        Undefined,
        Limit,
        Market,
        Stop,
        TrailingStop,
        Fok,
        StopLimit,
        TakeProfitLimit,
        TakeProfitMarket,
        ExchangeLimit,
        ExchangeMarket,
        ExchangeTrailingStop
    }
}