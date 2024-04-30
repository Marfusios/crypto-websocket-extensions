using System.Collections.Generic;
using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models;

/// <summary>
/// Info about changed order book
/// </summary>
public class TopNLevelsChangeInfo : OrderBookChangeInfo, ITopNLevelsChangeInfo
{
    /// <inheritdoc />
    public TopNLevelsChangeInfo(string pair, string pairOriginal,
        ICryptoQuotes quotes, IReadOnlyList<OrderBookLevel> levels, IReadOnlyList<OrderBookLevelBulk> sources,
        bool isSnapshot)
        : base(pair, pairOriginal, quotes, levels, sources, isSnapshot)
    {
    }

    /// <summary>
    /// The L2 snapshot of the top N bid/ask levels
    /// </summary>
    public L2Snapshot Snapshot { get; internal set; }
}
