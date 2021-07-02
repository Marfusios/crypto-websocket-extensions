using System.Diagnostics;
using System.Runtime;
using System.Threading;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;
using static Crypto.Websocket.Extensions.Tests.Helpers.OrderBookTestUtils;

namespace Crypto.Websocket.Extensions.Tests
{
    public class CryptoOrderBookL3PerformanceTests
    {
        readonly ITestOutputHelper _output;

        public CryptoOrderBookL3PerformanceTests(ITestOutputHelper output)
        {
            _output = output;

            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }

        [Fact]
        public void StreamPriceChanges_10kIterations_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockDataL3(pair, 1000);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3);
            var source = new OrderBookSourceMock(snapshot);

            ICryptoOrderBook orderBook = new CryptoOrderBook(pair, source, CryptoOrderBookType.L3);

            source.BufferEnabled = false;
            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var elapsedMs = StreamLevels(pair, source, orderBook, 10000, 100, 100);
            var msg = $"Elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.True(elapsedMs < 7000, msg);
        }

        [Fact]
        public void StreamPriceChanges_20kIterations_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockDataL3(pair, 1000);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3);
            var source = new OrderBookSourceMock(snapshot);

            ICryptoOrderBook orderBook = new CryptoOrderBook(pair, source, CryptoOrderBookType.L3);

            source.BufferEnabled = false;
            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var elapsedMs = StreamLevels(pair, source, orderBook, 20000, 100, 100);
            var msg = $"Elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.True(elapsedMs < 7000, msg);
        }

        [Fact]
        public void StreamPriceChanges_100kIterations_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockDataL3(pair, 1000);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3);
            var source = new OrderBookSourceMock(snapshot);

            ICryptoOrderBook orderBook = new CryptoOrderBook(pair, source, CryptoOrderBookType.L3);

            source.BufferEnabled = false;
            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var elapsedMs = StreamLevels(pair, source, orderBook, 100000, 100, 100);
            var msg = $"Elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.True(elapsedMs < 7000, msg);
        }

        [Fact]
        public void StreamPriceChanges_1mIterations_ShouldBeFast()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockDataL3(pair, 1000);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3);
            var source = new OrderBookSourceMock(snapshot);

            ICryptoOrderBook orderBook = new CryptoOrderBook(pair, source, CryptoOrderBookType.L3);

            source.BufferEnabled = false;
            source.LoadSnapshotEnabled = false;
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            source.StreamSnapshot();

            var elapsedMs = StreamLevels(pair, source, orderBook, 1000000, 100, 100);
            var msg = $"Elapsed time was: {elapsedMs} ms";
            _output.WriteLine(msg);

            Assert.True(elapsedMs < 7000, msg);
        }

        static long StreamLevels(string pair, OrderBookSourceMock source, ICryptoOrderBook book, int iterations, int maxBidPrice, int maxAskPrice, bool slowDown = false)
        {
            var bid = book.BidLevels[0];
            var ask = book.AskLevels[0];

            var sw = new Stopwatch();
            var iterationsSafe = iterations * 13;
            for (int i = 0; i < iterationsSafe; i += 13)
            {
                var newBidPrice = i % maxBidPrice;
                var newAskPrice = maxBidPrice + (i % maxAskPrice);

                // update levels
                var bulk = GetUpdateBulkL2(
                    CreateLevel(pair, newBidPrice, CryptoOrderSide.Bid, bid.Id),
                    CreateLevel(pair, newAskPrice, CryptoOrderSide.Ask, ask.Id)
                );
                sw.Start();
                source.StreamBulk(bulk);
                sw.Stop();

                if (slowDown && i % 1000 == 0)
                {
                    Thread.Sleep(10);
                }
            }

            return sw.ElapsedMilliseconds;
        }
    }
}
