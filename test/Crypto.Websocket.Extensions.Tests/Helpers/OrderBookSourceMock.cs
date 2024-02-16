using System;
using System.Linq;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crypto.Websocket.Extensions.Tests.Helpers
{
    public class OrderBookSourceMock : OrderBookSourceBase
    {
        private readonly OrderBookLevelBulk _snapshot;
        private readonly OrderBookLevelBulk[] _bulks;

        public int SnapshotCalledCount { get; private set; }
        public string SnapshotLastPair { get; private set; }

        public OrderBookSourceMock() : base(NullLogger.Instance)
        {
            BufferInterval = TimeSpan.FromMilliseconds(10);
        }

        public OrderBookSourceMock(OrderBookLevelBulk snapshot) : this()
        {
            BufferInterval = TimeSpan.FromMilliseconds(10);
            _snapshot = snapshot;
        }

        public OrderBookSourceMock(params OrderBookLevelBulk[] bulks) : this()
        {
            BufferInterval = TimeSpan.FromMilliseconds(10);
            _bulks = bulks;
        }

        public void StreamSnapshot()
        {
            StreamSnapshot(_snapshot);
        }

        public void StreamSnapshotRaw(OrderBookLevelBulk snapshot)
        {
            StreamSnapshot(snapshot);
        }

        public void StreamBulks()
        {
            foreach (var bulk in _bulks)
            {
                BufferData(bulk);
            }
        }

        public void StreamBulk(OrderBookLevelBulk bulk)
        {
            BufferData(bulk);
        }

        public override string ExchangeName => "mock";

        protected override Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count = 1000)
        {
            SnapshotCalledCount++;
            SnapshotLastPair = pair;

            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, new OrderBookLevel[0], CryptoOrderBookType.L2);

            return Task.FromResult(bulk);
        }

        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            return data.Cast<OrderBookLevelBulk>().ToArray();
        }
    }
}