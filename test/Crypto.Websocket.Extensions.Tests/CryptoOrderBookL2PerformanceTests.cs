using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Bitmex.Client.Websocket.Client;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Tests.data;
using Crypto.Websocket.Extensions.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using static Crypto.Websocket.Extensions.Tests.Helpers.OrderBookTestUtils;

namespace Crypto.Websocket.Extensions.Tests
{
    [Collection("Non-Parallel Collection")]
    public class CryptoOrderBookL2PerformanceTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string[] _rawFiles = {
            "data/bitmex_raw_xbtusd_2018-11-13.txt.gz"
        };

        public CryptoOrderBookL2PerformanceTests(ITestOutputHelper output)
        {
            _output = output;

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }

        [Fact]
        public void StreamLargeAmount_100Iterations_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            var orderBook = new CryptoOrderBookL2(pair, source);

            source.BufferEnabled = false;
            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var elapsedMs = StreamLevels(pair, source, 100, 500, 500);
            var msg = $"Elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.Equal(515, orderBook.BidLevels.Length);
            Assert.Equal(520, orderBook.AskLevels.Length);

            Assert.Equal(499, orderBook.BidPrice);
            Assert.Equal(500.4, orderBook.AskPrice);

            Assert.Equal(1499, orderBook.BidAmount);
            Assert.Equal(400, orderBook.AskAmount);

            Assert.True(elapsedMs < 100, msg);
        }

        [Fact]
        public void StreamLargeAmount_10kIterations_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            var orderBook = new CryptoOrderBookL2(pair, source);

            source.BufferEnabled = false;
            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var elapsedMs = StreamLevels(pair, source, 10000, 500, 500);
            var msg = $"Elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.Equal(601, orderBook.BidLevels.Length);
            Assert.Equal(596, orderBook.AskLevels.Length);

            Assert.Equal(499, orderBook.BidPrice);
            Assert.Equal(500, orderBook.AskPrice);

            Assert.Equal(10049, orderBook.BidAmount);
            Assert.Equal(12999, orderBook.AskAmount);

            Assert.True(elapsedMs < 1200, msg);
        }

        [Fact]
        public void StreamLargeAmount_20kIterations_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            var orderBook = new CryptoOrderBookL2(pair, source);

            source.BufferEnabled = false;
            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var elapsedMs = StreamLevels(pair, source, 20000, 500, 500);
            var msg = $"Elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.Equal(601, orderBook.BidLevels.Length);
            Assert.Equal(596, orderBook.AskLevels.Length);

            Assert.Equal(499, orderBook.BidPrice);
            Assert.Equal(500, orderBook.AskPrice);

            Assert.Equal(20049, orderBook.BidAmount);
            Assert.Equal(22999, orderBook.AskAmount);

            Assert.True(elapsedMs < 2400, msg);
        }

        [Fact]
        public void StreamLargeAmount_100kIterations_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            var orderBook = new CryptoOrderBookL2(pair, source);

            source.BufferEnabled = false;
            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var elapsedMs = StreamLevels(pair, source, 100000, 500, 500);
            var msg = $"Elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.True(elapsedMs < 7000, msg);
        }

#if DEBUG
        [Fact]
        public async Task StreamLargeAmount_100kIterations_WithBuffer_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            var orderBook = new CryptoOrderBookL2(pair, source);
            var endTime = DateTime.MinValue;

            orderBook.OrderBookUpdatedStream.Subscribe(x => endTime = DateTime.UtcNow);

            source.BufferEnabled = true;
            source.BufferInterval = TimeSpan.FromMilliseconds(0);

            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var startTime = DateTime.UtcNow;
            var elapsedInsertingMs = StreamLevels(pair, source, 100000, 500, 500, false);
            var msgInserting = $"Elapsed time for inserting was: {elapsedInsertingMs} ms";
            _output.WriteLine(msgInserting);

            await Task.Delay(1000);

            var elapsedEnd = endTime.Subtract(startTime).TotalMilliseconds;
            var msg = $"Elapsed time for processing was: {elapsedEnd} ms";
            _output.WriteLine(msg);

            Assert.True(elapsedEnd < 5000, msg);
            Assert.True(elapsedInsertingMs < 1000, msgInserting);
        }

        [Fact]
        public async Task StreamLargeAmount_100kIterations_WithBufferAndSlowdown_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            var orderBook = new CryptoOrderBookL2(pair, source);
            var endTime = DateTime.MinValue;

            orderBook.OrderBookUpdatedStream.Subscribe(x => endTime = DateTime.UtcNow);

            source.BufferEnabled = true;
            source.BufferInterval = TimeSpan.FromMilliseconds(1);

            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var startTime = DateTime.UtcNow;
            var elapsedInsertingMs = StreamLevels(pair, source, 100000, 500, 500, true);
            var msgInserting = $"Elapsed time for inserting was: {elapsedInsertingMs} ms";
            _output.WriteLine(msgInserting);

            await Task.Delay(1000);

            var elapsedEnd = endTime.Subtract(startTime).TotalMilliseconds;
            var msg = $"Elapsed time for processing was: {elapsedEnd} ms";
            _output.WriteLine(msg);

            Assert.True(elapsedEnd < 5000, msg);
            Assert.True(elapsedInsertingMs < 1000, msgInserting);
        }

        [Fact]
        public async Task StreamFromFile_ShouldBeFast()
        {
            var pair = "XBTUSD";
            var communicator = new RawFileCommunicator();
            communicator.FileNames = _rawFiles;

            var client = new BitmexWebsocketClient(communicator);
            var source = new BitmexOrderBookSource(client);
            source.LoadSnapshotEnabled = false;
            source.BufferEnabled = false;

            var orderBook = new CryptoOrderBookL2(pair, source);
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;

            var snapshotCount = 0;
            var diffCount = 0;

            source.OrderBookSnapshotStream.Subscribe(x => snapshotCount++);
            source.OrderBookStream.Subscribe(x => diffCount++);

            var sw = Stopwatch.StartNew();
            await communicator.Start();
            sw.Stop();

            var elapsedMs = sw.ElapsedMilliseconds;
            var msg = $"Processed snapshots: {snapshotCount}, diffs: {diffCount}, elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.True(elapsedMs < 7000, msg);
        }
#endif

        private static long StreamLevels(string pair, OrderBookSourceMock source, int iterations, int maxBidPrice, int maxAskCount, bool slowDown = false)
        {
            var sw = new Stopwatch();
            for (int i = 0; i < iterations; i++)
            {
                if (i % 10 == 0)
                {
                    // insert new levels
                    var bulk = GetInsertBulkL2(
                        CreateLevel(pair, (i % maxBidPrice) + 0.4, i + 50, CryptoOrderSide.Bid),
                        CreateLevel(pair, (Math.Max(i - 55, 1) % maxBidPrice) + 0.4, i + 600, CryptoOrderSide.Bid),
                        CreateLevel(pair, (maxBidPrice + (i % maxAskCount) + 0.4), i + 400, CryptoOrderSide.Ask),
                        CreateLevel(pair, (maxBidPrice + (Math.Min(i + 55, maxAskCount) % maxAskCount) + 0.4), i + 3000, CryptoOrderSide.Ask)
                    );
                    sw.Start();
                    source.StreamBulk(bulk);
                    sw.Stop();
                }
                else
                {
                    // update levels
                    var bulk = GetUpdateBulkL2(
                        CreateLevel(pair, (i % maxBidPrice), i + 50, CryptoOrderSide.Bid),
                        CreateLevel(pair, (Math.Max(i - 55, 1) % maxBidPrice), i + 600, CryptoOrderSide.Bid),
                        CreateLevel(pair, (maxBidPrice + (i % maxAskCount)), i + 400, CryptoOrderSide.Ask),
                        CreateLevel(pair, (maxBidPrice + (Math.Min(i + 55, maxAskCount) % maxAskCount)), i + 3000, CryptoOrderSide.Ask)
                    );
                    sw.Start();
                    source.StreamBulk(bulk);
                    sw.Stop();
                }

                if (slowDown && i % 1000 == 0)
                {
                    Thread.Sleep(10);
                }
            }

            return sw.ElapsedMilliseconds;
        }
    }
}
