namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models;

/// <inheritdoc />
public interface ITopNLevelsChangeInfo : IOrderBookChangeInfo
{
    /// <summary>
    /// A snapshot of the the orderbook.
    /// </summary>
    L2Snapshot Snapshot { get; }
}
