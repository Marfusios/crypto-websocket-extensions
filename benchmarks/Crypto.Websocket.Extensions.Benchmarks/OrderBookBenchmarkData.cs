using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.Utils;

namespace Crypto.Websocket.Extensions.Benchmarks;

internal static class OrderBookBenchmarkData
{
    public const string Pair = "BTC/USD";
    public const int LevelsPerSide = 500;
    public const int DiffCount = 1000;

    public static readonly string PairClean = CryptoPairsHelper.Clean(Pair);
    public static readonly OrderBookLevelBulk Snapshot = CreateSnapshot(PairClean, LevelsPerSide);
    public static readonly OrderBookLevelBulk[] Diffs = CreateDiffs(PairClean, DiffCount, LevelsPerSide, LevelsPerSide);

    private static OrderBookLevelBulk CreateSnapshot(string pair, int count)
    {
        var levels = new OrderBookLevel[count * 2];
        var index = 0;

        for (var i = 0; i < count; i++)
            levels[index++] = CreateLevel(pair, i, count * 2 + i, CryptoOrderSide.Bid);

        for (var i = count * 2; i > count; i--)
            levels[index++] = CreateLevel(pair, i, count * 4 + i, CryptoOrderSide.Ask);

        return new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
    }

    private static OrderBookLevelBulk[] CreateDiffs(string pair, int iterations, int maxBidPrice, int maxAskCount)
    {
        var bulks = new OrderBookLevelBulk[iterations];
        for (var i = 0; i < iterations; i++)
        {
            var levels = new[]
            {
                i % 10 == 0
                    ? CreateLevel(pair, (i % maxBidPrice) + 0.4, i + 50, CryptoOrderSide.Bid)
                    : CreateLevel(pair, i % maxBidPrice, i + 50, CryptoOrderSide.Bid),
                i % 10 == 0
                    ? CreateLevel(pair, (Math.Max(i - 55, 1) % maxBidPrice) + 0.4, i + 600, CryptoOrderSide.Bid)
                    : CreateLevel(pair, Math.Max(i - 55, 1) % maxBidPrice, i + 600, CryptoOrderSide.Bid),
                i % 10 == 0
                    ? CreateLevel(pair, maxBidPrice + (i % maxAskCount) + 0.4, i + 400, CryptoOrderSide.Ask)
                    : CreateLevel(pair, maxBidPrice + (i % maxAskCount), i + 400, CryptoOrderSide.Ask),
                i % 10 == 0
                    ? CreateLevel(pair, maxBidPrice + (Math.Min(i + 55, maxAskCount) % maxAskCount) + 0.4, i + 3000, CryptoOrderSide.Ask)
                    : CreateLevel(pair, maxBidPrice + (Math.Min(i + 55, maxAskCount) % maxAskCount), i + 3000, CryptoOrderSide.Ask)
            };

            var action = i % 10 == 0 ? OrderBookAction.Insert : OrderBookAction.Update;
            bulks[i] = new OrderBookLevelBulk(action, levels, CryptoOrderBookType.L2);
        }

        return bulks;
    }

    private static OrderBookLevel CreateLevel(string pair, double? price, double? amount, CryptoOrderSide side)
    {
        return new OrderBookLevel(
            $"{price}-{(side == CryptoOrderSide.Bid ? "bid" : "ask")}",
            side,
            price,
            amount,
            3,
            pair);
    }
}
