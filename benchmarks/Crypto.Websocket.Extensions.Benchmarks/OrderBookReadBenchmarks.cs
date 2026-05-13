using BenchmarkDotNet.Attributes;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[RankColumn]
public class OrderBookReadBenchmarks
{
    private BenchmarkOrderBookSource _sourceL2 = null!;
    private BenchmarkOrderBookSource _sourceAll = null!;
    private CryptoOrderBookL2 _orderBookL2 = null!;
    private CryptoOrderBook _orderBook = null!;

    [GlobalSetup]
    public void Setup()
    {
        _sourceL2 = new BenchmarkOrderBookSource();
        _orderBookL2 = new CryptoOrderBookL2(OrderBookBenchmarkData.Pair, _sourceL2)
        {
            SnapshotReloadEnabled = false,
            ValidityCheckEnabled = false,
            NotifyForLevelAndAbove = 0
        };
        Seed(_sourceL2);

        _sourceAll = new BenchmarkOrderBookSource();
        _orderBook = new CryptoOrderBook(OrderBookBenchmarkData.Pair, _sourceAll, CryptoOrderBookType.L2)
        {
            SnapshotReloadEnabled = false,
            ValidityCheckEnabled = false,
            NotifyForLevelAndAbove = 0
        };
        Seed(_sourceAll);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _orderBookL2.Dispose();
        _orderBook.Dispose();
        _sourceL2.Dispose();
        _sourceAll.Dispose();
    }

    [Benchmark(Baseline = true, Description = "OrderBookL2 BidLevels")]
    public int OrderBookL2_BidLevels()
    {
        return _orderBookL2.BidLevels.Length;
    }

    [Benchmark(Description = "OrderBookL2 All Levels")]
    public int OrderBookL2_AllLevels()
    {
        return _orderBookL2.Levels.Length;
    }

    [Benchmark(Description = "OrderBookL2 FindBidLevelByPrice")]
    public OrderBookLevel? OrderBookL2_FindBid()
    {
        return _orderBookL2.FindBidLevelByPrice(499);
    }

    [Benchmark(Description = "OrderBook BidLevels")]
    public int OrderBook_BidLevels()
    {
        return _orderBook.BidLevels.Length;
    }

    [Benchmark(Description = "OrderBook All Levels")]
    public int OrderBook_AllLevels()
    {
        return _orderBook.Levels.Length;
    }

    [Benchmark(Description = "OrderBook FindBidLevelByPrice")]
    public OrderBookLevel? OrderBook_FindBid()
    {
        return _orderBook.FindBidLevelByPrice(499);
    }

    private static void Seed(BenchmarkOrderBookSource source)
    {
        source.StreamSnapshotRaw(OrderBookBenchmarkData.Snapshot);

        foreach (var bulk in OrderBookBenchmarkData.Diffs)
            source.StreamBulk(bulk);
    }
}
