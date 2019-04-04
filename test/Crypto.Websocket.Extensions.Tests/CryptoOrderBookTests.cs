using System;
using System.Collections.Generic;
using System.Linq;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Utils;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests
{
    public class CryptoOrderBookTests
    {
        [Fact]
        public void StreamingSnapshot_ShouldHandleCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var source = new OrderBookSourceMock(data);

            var orderBook = new CryptoOrderBook(pair, source);

            source.StreamSnapshot();

            Assert.Equal(500, orderBook.BidLevels.Length);
            Assert.Equal(500, orderBook.AskLevels.Length);

            Assert.Equal(499, orderBook.BidPrice);
            Assert.Equal(1499, orderBook.BidLevels.First().Amount);

            Assert.Equal(501, orderBook.AskPrice);
            Assert.Equal(2501, orderBook.AskLevels.First().Amount);

            var levels = orderBook.Levels;
            foreach (var level in levels)
            {
                Assert.Equal(CryptoPairsHelper.Clean(pair), level.Pair);
            }
        }

        [Fact]
        public void StreamingSnapshot_DifferentPairs_ShouldHandleCorrectly()
        {
            var pair1 = "BTC/USD";
            var pair2 = "ETH/BTC";
            var data1 = GetOrderBookSnapshotMockData(pair1, 500);
            var data2 = GetOrderBookSnapshotMockData(pair2, 200);
            var data = data2.Concat(data1).ToArray();
            var source = new OrderBookSourceMock(data);

            var orderBook = new CryptoOrderBook(pair1, source);

            source.StreamSnapshot();

            Assert.Equal(500, orderBook.BidLevels.Length);
            Assert.Equal(500, orderBook.AskLevels.Length);

            Assert.Equal(499, orderBook.BidLevels.First().Price);
            Assert.Equal(1499, orderBook.BidLevels.First().Amount);

            Assert.Equal(501, orderBook.AskLevels.First().Price);
            Assert.Equal(2501, orderBook.AskLevels.First().Amount);
            
            var levels = orderBook.Levels;
            foreach (var level in levels)
            {
                Assert.Equal(CryptoPairsHelper.Clean(pair1), level.Pair);
            }
        }

        [Fact]
        public void FindLevel_ShouldReturnCorrectValue()
        {
            var pair1 = "BTC/USD";
            var pair2 = "ETH/BTC";
            var data1 = GetOrderBookSnapshotMockData(pair1, 500);
            var data2 = GetOrderBookSnapshotMockData(pair2, 200);
            var data = data2.Concat(data1).ToArray();
            var source = new OrderBookSourceMock(data);

            var orderBook = new CryptoOrderBook(pair1, source);

            source.StreamSnapshot();

            Assert.Equal(1000, orderBook.FindBidLevelByPrice(0)?.Amount);
            Assert.Equal(1000, orderBook.FindBidLevelById("0-bid")?.Amount);

            Assert.Equal(3000, orderBook.FindAskLevelByPrice(1000)?.Amount);
            Assert.Equal(3000, orderBook.FindAskLevelById("1000-ask")?.Amount);
        }

        [Fact]
        public void StreamingDiff_BeforeSnapshot_ShouldDoNothing()
        {
            var pair = "BTC/USD";
            var source = new OrderBookSourceMock();
            var orderBook = new CryptoOrderBook(pair, source);

            source.StreamBulk(GetInsertBulk(
                CreateLevel(pair, 100, 50, CryptoSide.Bid),
                CreateLevel(pair, 55, 600, CryptoSide.Bid),
                CreateLevel(pair, 105, 400, CryptoSide.Ask),
                CreateLevel(pair, 200, 3000, CryptoSide.Ask)
            ));

            Assert.Empty(orderBook.BidLevels);
            Assert.Empty(orderBook.AskLevels);

            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.AskPrice);
        }

        [Fact]
        public void StreamingDiff_ShouldHandleCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var source = new OrderBookSourceMock(data);

            var orderBook = new CryptoOrderBook(pair, source);

            source.StreamSnapshot();

            source.StreamBulk(GetInsertBulk(
                CreateLevel(pair, 499.4, 50, CryptoSide.Bid),
                CreateLevel(pair, 498.3, 600, CryptoSide.Bid),
                CreateLevel(pair, 300.33, 3350, CryptoSide.Bid),
                CreateLevel(pair, 500.2, 400, CryptoSide.Ask),
                CreateLevel(pair, 503.1, 3000, CryptoSide.Ask),
                CreateLevel(pair, 800.123, 1234, CryptoSide.Ask)
            ));

            source.StreamBulk(GetUpdateBulk(
                CreateLevel(pair, 499, 33, CryptoSide.Bid),
                CreateLevel(pair, 450, 33, CryptoSide.Bid),
                CreateLevel(pair, 501, 32, CryptoSide.Ask),
                CreateLevel(pair, 503.1, 32, CryptoSide.Ask),

                CreateLevel(null, 100, null, CryptoSide.Bid),
                CreateLevel(null, 900, null, CryptoSide.Ask)
            ));

            source.StreamBulk(GetDeleteBulk(
                CreateLevel(0, CryptoSide.Bid),
                CreateLevel(1, CryptoSide.Bid),
                CreateLevel(1000, CryptoSide.Ask),
                CreateLevel(999, CryptoSide.Ask)
            ));

            Assert.NotEmpty(orderBook.BidLevels);
            Assert.NotEmpty(orderBook.AskLevels);

            Assert.Equal(501, orderBook.BidLevels.Length);
            Assert.Equal(501, orderBook.AskLevels.Length);

            Assert.Equal(499.4, orderBook.BidPrice);
            Assert.Equal(500.2, orderBook.AskPrice);

            Assert.Equal(33, orderBook.FindBidLevelByPrice(499)?.Amount);
            Assert.Equal(33, orderBook.FindBidLevelByPrice(450)?.Amount);

            Assert.Equal(32, orderBook.FindAskLevelByPrice(501)?.Amount);
            Assert.Equal(32, orderBook.FindAskLevelByPrice(503.1)?.Amount);

            var notCompleteBid = orderBook.FindBidLevelByPrice(100);
            Assert.Equal(CryptoPairsHelper.Clean(pair), notCompleteBid.Pair);
            Assert.Equal(1100, notCompleteBid.Amount);
            Assert.Equal(3, notCompleteBid.Count);

            var notCompleteAsk = orderBook.FindAskLevelByPrice(900);
            Assert.Equal(CryptoPairsHelper.Clean(pair), notCompleteAsk.Pair);
            Assert.Equal(2900, notCompleteAsk.Amount);
            Assert.Equal(3, notCompleteAsk.Count);

            Assert.Null(orderBook.FindBidLevelByPrice(0));
            Assert.Null(orderBook.FindBidLevelByPrice(1));
            Assert.Null(orderBook.FindAskLevelByPrice(1000));
            Assert.Null(orderBook.FindAskLevelByPrice(999));
        }

        [Fact]
        public void StreamingData_ShouldNotifyCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var source = new OrderBookSourceMock(data);

            var notificationCount = 0;
            var notificationBidAskCount = 0;

            var orderBook = new CryptoOrderBook(pair, source);

            orderBook.OrderBookUpdatedStream.Subscribe(_ => notificationCount++);
            orderBook.BidAskUpdatedStream.Subscribe(_ => notificationBidAskCount++);

            source.StreamSnapshot();

            source.StreamBulk(GetInsertBulk(
                CreateLevel(pair, 499.4, 50, CryptoSide.Bid)
            ));


            source.StreamBulk(GetInsertBulk(
                CreateLevel(pair, 499.5, 600, CryptoSide.Bid),
                CreateLevel(pair, 300.33, 3350, CryptoSide.Bid),
                CreateLevel(pair, 500.2, 400, CryptoSide.Ask)
            ));

            source.StreamBulk(GetInsertBulk(
                CreateLevel(pair, 503.1, 3000, CryptoSide.Ask),
                CreateLevel(pair, 800.123, 1234, CryptoSide.Ask)
            ));

            source.StreamBulk(GetUpdateBulk(
                CreateLevel(pair, 499, 33, CryptoSide.Bid),
                CreateLevel(pair, 450, 33, CryptoSide.Bid),
                CreateLevel(pair, 501, 32, CryptoSide.Ask),
                CreateLevel(pair, 503.1, 32, CryptoSide.Ask),

                CreateLevel(null, 100, null, CryptoSide.Bid),
                CreateLevel(null, 900, null, CryptoSide.Ask)
            ));

            source.StreamBulk(GetDeleteBulk(
                CreateLevel(0, CryptoSide.Bid),
                CreateLevel(1, CryptoSide.Bid),
                CreateLevel(1000, CryptoSide.Ask),
                CreateLevel(999, CryptoSide.Ask)
            ));

            Assert.Equal(6, notificationCount);
            Assert.Equal(3, notificationBidAskCount);
        }


        private OrderBookLevel[] GetOrderBookSnapshotMockData(string pair, int count)
        {
            var result = new List<OrderBookLevel>();

            for (int i = 0; i < count; i++)
            {
                var bid = CreateLevel(pair, i, count * 2 + i, CryptoSide.Bid);
                result.Add(bid);
            }

            
            for (int i = count*2; i > count; i--)
            {
                var ask = CreateLevel(pair, i, count * 4 + i, CryptoSide.Ask);
                result.Add(ask);
            }

            return result.ToArray();
        }

        private OrderBookLevelBulk GetInsertBulk(params OrderBookLevel[] levels)
        {
            return new OrderBookLevelBulk(OrderBookAction.Insert, levels);
        }

        private OrderBookLevelBulk GetUpdateBulk(params OrderBookLevel[] levels)
        {
            return new OrderBookLevelBulk(OrderBookAction.Update, levels);
        }

        private OrderBookLevelBulk GetDeleteBulk(params OrderBookLevel[] levels)
        {
            return new OrderBookLevelBulk(OrderBookAction.Delete, levels);
        }

        private OrderBookLevel CreateLevel(string pair, double price, double? amount, CryptoSide side)
        {
            return new OrderBookLevel(
                CreateKey(price,side),
                side,
                price,
                amount,
                3,
                pair == null ? null : CryptoPairsHelper.Clean(pair)
            );
        }

        private OrderBookLevel CreateLevel(double price, CryptoSide side)
        {
            return new OrderBookLevel(
                CreateKey(price,side),
                side,
                null,
                null,
                null,
                null
            );
        }

        private string CreateKey(double price, CryptoSide side)
        {
            var sideSafe = side == CryptoSide.Bid ? "bid" : "ask";
            return $"{price}-{sideSafe}";
        }

        private class OrderBookSourceMock : OrderBookLevel2SourceBase
        {
            private readonly OrderBookLevel[] _snapshot;
            private readonly OrderBookLevelBulk[] _bulks;

            public OrderBookSourceMock()
            {
                
            }

            public OrderBookSourceMock(params OrderBookLevel[] snapshot)
            {
                _snapshot = snapshot;
            }

            public OrderBookSourceMock(params OrderBookLevelBulk[] bulks)
            {
                _bulks = bulks;
            }

            public void StreamSnapshot()
            {
                OrderBookSnapshotSubject.OnNext(_snapshot);
            }

            public void StreamBulks()
            {
                foreach (var bulk in _bulks)
                {
                    OrderBookSubject.OnNext(bulk);
                }
            }

            public void StreamBulk(OrderBookLevelBulk bulk)
            {
                OrderBookSubject.OnNext(bulk);
            }
        }
    }
}
