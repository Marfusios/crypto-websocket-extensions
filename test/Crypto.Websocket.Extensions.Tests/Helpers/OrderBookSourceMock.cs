using System;
using System.Linq;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;

namespace Crypto.Websocket.Extensions.Tests.Helpers
{
    public class OrderBookSourceMock : OrderBookLevel2SourceBase
    {
        private readonly OrderBookLevel[] _snapshot;
        private readonly OrderBookLevelBulk[] _bulks;

        public int SnapshotCalledCount { get; private set; }
        public string SnapshotLastPair { get; private set; }

        public OrderBookSourceMock()
        {
            BufferInterval = TimeSpan.FromMilliseconds(10);
        }

        public OrderBookSourceMock(params OrderBookLevel[] snapshot)
        {
            BufferInterval = TimeSpan.FromMilliseconds(10);
            _snapshot = snapshot;
        }

        public OrderBookSourceMock(params OrderBookLevelBulk[] bulks)
        {
            BufferInterval = TimeSpan.FromMilliseconds(10);
            _bulks = bulks;
        }

        public void StreamSnapshot()
        {
            StreamSnapshot(_snapshot);
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

        protected override Task<OrderBookLevel[]> LoadSnapshotInternal(string pair, int count = 1000)
        {
            SnapshotCalledCount++;
            SnapshotLastPair = pair;

            return Task.FromResult(new OrderBookLevel[0]);
        }

        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            return data.Cast<OrderBookLevelBulk>().ToArray();
        }
    }
}