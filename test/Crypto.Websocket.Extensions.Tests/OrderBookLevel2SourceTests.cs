using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Utils;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests
{
    public class OrderBookLevel2SourceTests
    {
        [Fact]
        public async Task Buffering_ShouldBeEnabledByDefault_StreamDataAfterSomeTime()
        {
            var snapshot = new[]
            {
                new OrderBookLevel("1", CryptoSide.Bid, 100, 5, 1, "BTCUSD"),
                new OrderBookLevel("2", CryptoSide.Ask, 101, 50, 2, "BTCUSD"),
            };

            var bulks = new[]
            {
                new OrderBookLevelBulk(OrderBookAction.Update, snapshot),
                new OrderBookLevelBulk(OrderBookAction.Delete, snapshot)
            };

            var source = GetMock(snapshot, bulks);

            OrderBookLevel[] receivedSnapshot = null;
            OrderBookLevelBulk[] receivedBulks = null;

            source.OrderBookSnapshotStream.Subscribe(x => receivedSnapshot = x);
            source.OrderBookStream.Subscribe(x => receivedBulks = x);

            await source.LoadSnapshot("BTCUSD");
            source.StreamData();

            Assert.NotNull(receivedSnapshot);
            Assert.Null(receivedBulks);

            await Task.Delay(500);

            Assert.NotNull(receivedSnapshot);
            Assert.NotNull(receivedBulks);
        }

        [Fact]
        public async Task Buffering_Disable_ShouldWork()
        {
            var snapshot = new[]
            {
                new OrderBookLevel("1", CryptoSide.Bid, 100, 5, 1, "BTCUSD"),
                new OrderBookLevel("2", CryptoSide.Ask, 101, 50, 2, "BTCUSD"),
            };

            var bulks = new[]
            {
                new OrderBookLevelBulk(OrderBookAction.Update, snapshot),
                new OrderBookLevelBulk(OrderBookAction.Delete, snapshot)
            };

            var source = GetMock(snapshot, bulks);
            source.BufferEnabled = false;

            OrderBookLevel[] receivedSnapshot = null;
            OrderBookLevelBulk[] receivedBulks = null;

            source.OrderBookSnapshotStream.Subscribe(x => receivedSnapshot = x);
            source.OrderBookStream.Subscribe(x => receivedBulks = x);

            await source.LoadSnapshot("BTCUSD");
            source.StreamData();

            Assert.NotNull(receivedSnapshot);
            Assert.NotNull(receivedBulks);
        }

        private MockSource GetMock(OrderBookLevel[] snapshot, OrderBookLevelBulk[] bulks)
        {
            return new MockSource(snapshot, bulks);
        }
    }

    public class MockSource : OrderBookLevel2SourceBase
    {
        private readonly OrderBookLevel[] _snapshotLevels;
        private readonly OrderBookLevelBulk[] _bulks;

        public MockSource(OrderBookLevel[] snapshotLevels, OrderBookLevelBulk[] bulks)
        {
            _snapshotLevels = snapshotLevels;
            _bulks = bulks;
        }

        public override string ExchangeName => "mock";

        protected override Task<OrderBookLevel[]> LoadSnapshotInternal(string pair, int count = 1000)
        {
            return Task.FromResult(_snapshotLevels);
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
