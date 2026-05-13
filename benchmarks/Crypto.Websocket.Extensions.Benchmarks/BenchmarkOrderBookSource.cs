using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crypto.Websocket.Extensions.Benchmarks;

internal sealed class BenchmarkOrderBookSource : OrderBookSourceBase
{
    public BenchmarkOrderBookSource() : base(NullLogger.Instance)
    {
        BufferEnabled = false;
        LoadSnapshotEnabled = false;
    }

    public override string ExchangeName => "benchmark";

    public void StreamSnapshotRaw(OrderBookLevelBulk snapshot)
    {
        StreamSnapshot(snapshot);
    }

    public void StreamBulk(OrderBookLevelBulk bulk)
    {
        BufferData(bulk);
    }

    protected override Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000)
    {
        return Task.FromResult<OrderBookLevelBulk?>(null);
    }

    protected override OrderBookLevelBulk[] ConvertData(object data)
    {
        return data is OrderBookLevelBulk bulk
            ? new[] { bulk }
            : Array.Empty<OrderBookLevelBulk>();
    }

    protected override bool TryConvertData(object data, out OrderBookLevelBulk? bulk)
    {
        bulk = data as OrderBookLevelBulk;
        return bulk != null;
    }

    protected override OrderBookLevelBulk[] ConvertData(object[] data)
    {
        if (data.Length == 0)
            return Array.Empty<OrderBookLevelBulk>();

        if (data.Length == 1 && data[0] is OrderBookLevelBulk single)
            return new[] { single };

        var result = new OrderBookLevelBulk[data.Length];
        var count = 0;
        foreach (var item in data)
        {
            if (item is OrderBookLevelBulk bulk)
                result[count++] = bulk;
        }

        if (count == result.Length)
            return result;

        Array.Resize(ref result, count);
        return result;
    }
}
