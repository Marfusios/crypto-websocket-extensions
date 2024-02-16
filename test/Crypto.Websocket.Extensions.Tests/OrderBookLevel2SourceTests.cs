using System;
using System.Threading;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests
{
    public class OrderBookLevel2SourceTests
    {
#if DEBUG
        [Fact]
        public async Task Buffering_ShouldBeEnabledByDefault_StreamDataAfterSomeTime()
        {
            var now = CryptoDateUtils.ConvertFromUnixSeconds(1577575307.123451);

            var snapshotLevels = new[]
            {
                new OrderBookLevel("1", CryptoOrderSide.Bid, 100, 5, 1, "BTCUSD"),
                new OrderBookLevel("2", CryptoOrderSide.Ask, 101, 50, 2, "BTCUSD"),
            };
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, snapshotLevels, CryptoOrderBookType.L2)
            {
                ServerSequence = 3,
                ServerTimestamp = now,
                ExchangeName = "test"
            };

            var bulks = new[]
            {
                new OrderBookLevelBulk(OrderBookAction.Update, snapshotLevels, CryptoOrderBookType.L2)
                {
                    ServerSequence = 4,
                    ServerTimestamp = now.AddMilliseconds(1),
                    ExchangeName = "test"
                },
                new OrderBookLevelBulk(OrderBookAction.Delete, snapshotLevels, CryptoOrderBookType.L2)
                {
                    ServerSequence = 5,
                    ServerTimestamp = now.AddMilliseconds(2),
                    ExchangeName = "test"
                }
            };

            var source = GetMock(snapshot, bulks);
            source.BufferInterval = TimeSpan.FromMilliseconds(100);

            OrderBookLevelBulk receivedSnapshot = null;
            OrderBookLevelBulk[] receivedBulks = null;

            source.OrderBookSnapshotStream.Subscribe(x => receivedSnapshot = x);
            source.OrderBookStream.Subscribe(x => receivedBulks = x);

            await source.LoadSnapshot("BTCUSD");
            source.StreamData();

            Assert.NotNull(receivedSnapshot);
            Assert.Equal("test", receivedSnapshot.ExchangeName);
            Assert.Equal(3, receivedSnapshot.ServerSequence);
            Assert.Equal(now, receivedSnapshot.ServerTimestamp);
            Assert.Null(receivedBulks);

            await Task.Delay(500);

            Assert.NotNull(receivedSnapshot);
            Assert.NotNull(receivedBulks);

            Assert.Equal("test", receivedBulks[1].ExchangeName);
            Assert.Equal(5, receivedBulks[1].ServerSequence);
            Assert.Equal("1577575307.125451", receivedBulks[1].ServerTimestamp.ToUnixSecondsString());
        }
#endif

        [Fact]
        public async Task Buffering_Disable_ShouldWork()
        {
            var now = CryptoDateUtils.ConvertFromUnixSeconds(1577575307.123451);

            var snapshotLevels = new[]
            {
                new OrderBookLevel("1", CryptoOrderSide.Bid, 100, 5, 1, "BTCUSD"),
                new OrderBookLevel("2", CryptoOrderSide.Ask, 101, 50, 2, "BTCUSD"),
            };
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, snapshotLevels, CryptoOrderBookType.L2)
            {
                ServerSequence = 3,
                ServerTimestamp = now,
                ExchangeName = "test"
            };

            var bulks = new[]
            {
                new OrderBookLevelBulk(OrderBookAction.Update, snapshotLevels, CryptoOrderBookType.L2)
                {
                    ServerSequence = 4,
                    ServerTimestamp = now.AddMilliseconds(1),
                    ExchangeName = "test"
                },
                new OrderBookLevelBulk(OrderBookAction.Delete, snapshotLevels, CryptoOrderBookType.L2)
                {
                    ServerSequence = 5,
                    ServerTimestamp = now.AddMilliseconds(2),
                    ExchangeName = "test"
                }
            };

            var source = GetMock(snapshot, bulks);
            source.BufferEnabled = false;

            OrderBookLevelBulk receivedSnapshot = null;
            OrderBookLevelBulk[] receivedBulks = null;

            source.OrderBookSnapshotStream.Subscribe(x => receivedSnapshot = x);
            source.OrderBookStream.Subscribe(x => receivedBulks = x);

            await source.LoadSnapshot("BTCUSD");
            source.StreamData();

            Assert.NotNull(receivedSnapshot);
            Assert.NotNull(receivedBulks);

            Assert.Equal("test", receivedSnapshot.ExchangeName);
            Assert.Equal(3, receivedSnapshot.ServerSequence);
            Assert.Equal(now, receivedSnapshot.ServerTimestamp);

            Assert.Equal("test", receivedBulks[1].ExchangeName);
            Assert.Equal(5, receivedBulks[1].ServerSequence);
            Assert.Equal("1577575307.125451", receivedBulks[1].ServerTimestamp.ToUnixSecondsString());
        }

#if DEBUG
        [Fact]
        public async Task Buffering_ShouldStreamOneByOne()
        {
            var now = CryptoDateUtils.ConvertFromUnixSeconds(1577575307.123456);

            var snapshotLevels = new[]
            {
                new OrderBookLevel("1", CryptoOrderSide.Bid, 100, 5, 1, "BTCUSD"),
                new OrderBookLevel("2", CryptoOrderSide.Ask, 101, 50, 2, "BTCUSD"),
            };
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, snapshotLevels, CryptoOrderBookType.L2)
            {
                ServerSequence = 3,
                ServerTimestamp = now,
                ExchangeName = "test"
            };

            var bulks = new[]
            {
                new OrderBookLevelBulk(OrderBookAction.Update, snapshotLevels, CryptoOrderBookType.L2)
                {
                    ServerSequence = 4,
                    ServerTimestamp = now.AddMilliseconds(1),
                    ExchangeName = "test"
                },
                new OrderBookLevelBulk(OrderBookAction.Delete, snapshotLevels, CryptoOrderBookType.L2)
                {
                    ServerSequence = 5,
                    ServerTimestamp = now.AddMilliseconds(2),
                    ExchangeName = "test"
                }
            };

            var source = GetMock(snapshot, bulks);
            source.BufferInterval = TimeSpan.FromMilliseconds(50);

            OrderBookLevelBulk receivedSnapshot = null;
            OrderBookLevelBulk[] receivedBulks = null;
            var receivedCount = 0;

            source.OrderBookSnapshotStream.Subscribe(x => receivedSnapshot = x);
            source.OrderBookStream.Subscribe(x =>
            {
                receivedBulks = x;
                receivedCount++;
                Thread.Sleep(2000);
            });

            await source.LoadSnapshot("BTCUSD");
            source.StreamData();

            Assert.NotNull(receivedSnapshot);
            Assert.Null(receivedBulks);
            Assert.Equal(0, receivedCount);

            await Task.Delay(100);

            Assert.NotNull(receivedSnapshot);
            Assert.NotNull(receivedBulks);
            Assert.Equal(1, receivedCount);

            source.StreamData();
            source.StreamData();
            await Task.Delay(100);
            Assert.Equal(1, receivedCount);

            source.StreamData();
            await Task.Delay(2200);
            Assert.Equal(2, receivedCount);
        }
#endif

        private MockSource GetMock(OrderBookLevelBulk snapshot, OrderBookLevelBulk[] bulks)
        {
            return new MockSource(snapshot, bulks);
        }
    }

    public class MockSource : OrderBookSourceBase
    {
        private readonly OrderBookLevelBulk _snapshot;
        private readonly OrderBookLevelBulk[] _bulks;

        public MockSource(OrderBookLevelBulk snapshot, OrderBookLevelBulk[] bulks) : base(NullLogger.Instance)
        {
            _snapshot = snapshot;
            _bulks = bulks;
        }

        public override string ExchangeName => "mock";

        protected override Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count = 1000)
        {
            return Task.FromResult(_snapshot);
        }

        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            return _bulks;
        }

        public void StreamData()
        {
            foreach (var bulk in _bulks)
            {
                BufferData(bulk);
            }
        }
    }
}
