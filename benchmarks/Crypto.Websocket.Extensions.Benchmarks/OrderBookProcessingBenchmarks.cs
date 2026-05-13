using BenchmarkDotNet.Attributes;
using Crypto.Websocket.Extensions.Core.OrderBooks;

namespace Crypto.Websocket.Extensions.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[RankColumn]
public class OrderBookProcessingBenchmarks
{
    [Benchmark(Baseline = true, Description = "OrderBookL2 process diffs", OperationsPerInvoke = OrderBookBenchmarkData.DiffCount)]
    public double OrderBookL2_ProcessDiffs()
    {
        return ProcessDiffs(createL2: true, subscribeToUpdates: false);
    }

    [Benchmark(Description = "OrderBookL2 process diffs with observer", OperationsPerInvoke = OrderBookBenchmarkData.DiffCount)]
    public double OrderBookL2_ProcessDiffs_WithObserver()
    {
        return ProcessDiffs(createL2: true, subscribeToUpdates: true);
    }

    [Benchmark(Description = "OrderBook process L2 diffs", OperationsPerInvoke = OrderBookBenchmarkData.DiffCount)]
    public double OrderBook_ProcessDiffs()
    {
        return ProcessDiffs(createL2: false, subscribeToUpdates: false);
    }

    [Benchmark(Description = "OrderBook process L2 diffs with observer", OperationsPerInvoke = OrderBookBenchmarkData.DiffCount)]
    public double OrderBook_ProcessDiffs_WithObserver()
    {
        return ProcessDiffs(createL2: false, subscribeToUpdates: true);
    }

    private static double ProcessDiffs(bool createL2, bool subscribeToUpdates)
    {
        using var source = new BenchmarkOrderBookSource();
        using var orderBook = CreateOrderBook(createL2, source);

        var notifications = 0;
        using var subscription = subscribeToUpdates
            ? orderBook.OrderBookUpdatedStream.Subscribe(_ => notifications++)
            : null;

        source.StreamSnapshotRaw(OrderBookBenchmarkData.Snapshot);

        foreach (var bulk in OrderBookBenchmarkData.Diffs)
            source.StreamBulk(bulk);

        return orderBook.BidPrice + orderBook.AskPrice + orderBook.BidAmount + orderBook.AskAmount + notifications;
    }

    private static ICryptoOrderBook CreateOrderBook(bool createL2, BenchmarkOrderBookSource source)
    {
        ICryptoOrderBook orderBook = createL2
            ? new CryptoOrderBookL2(OrderBookBenchmarkData.Pair, source)
            : new CryptoOrderBook(OrderBookBenchmarkData.Pair, source, CryptoOrderBookType.L2);

        orderBook.SnapshotReloadEnabled = false;
        orderBook.ValidityCheckEnabled = false;
        orderBook.NotifyForLevelAndAbove = 0;
        return orderBook;
    }
}
