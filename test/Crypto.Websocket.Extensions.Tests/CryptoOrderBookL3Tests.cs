using System;
using System.Linq;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Tests.Helpers;
using Xunit;

using static Crypto.Websocket.Extensions.Tests.Helpers.OrderBookTestUtils;

namespace Crypto.Websocket.Extensions.Tests
{
    [Collection("Non-Parallel Collection")]
    public class CryptoOrderBookL3Tests
    {
        [Fact]
        public void TopNLevelsUpdated_ShouldFilterCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockDataL3(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3);
            var source = new OrderBookSourceMock(snapshot);
            source.BufferEnabled = false;

            var orderBook = new CryptoOrderBook(pair, source, CryptoOrderBookType.L3)
            {
                 NotifyForLevelAndAbove = 5
            };

            var orderBookUpdatedCount = 0;
            var topNLevelsUpdatedCount = 0;
            ITopNLevelsChangeInfo topNLevelsChangeInfo = null;

            orderBook.OrderBookUpdatedStream.Subscribe(_ =>
            {
                orderBookUpdatedCount++;
            });
            orderBook.TopNLevelsUpdatedStream.Subscribe(x =>
            {
                topNLevelsUpdatedCount++;
                topNLevelsChangeInfo = x;
            });

            source.StreamSnapshot();

            source.StreamBulk(GetInsertBulk(CryptoOrderBookType.L3,
                CreateLevel(pair, 49.4, 50, CryptoOrderSide.Bid, null, "bid1"),
                CreateLevel(pair, 49.4, 25, CryptoOrderSide.Bid, null, "bid2"),
                CreateLevel(pair, 48.3, 600, CryptoOrderSide.Bid, null, "bid3"),
                CreateLevel(pair, 30.33, 3350, CryptoOrderSide.Bid, null, "bid4"),
                CreateLevel(pair, 49.9, 400, CryptoOrderSide.Ask, null, "ask1"),
                CreateLevel(pair, 49.9, 200, CryptoOrderSide.Ask, null, "ask2"),
                CreateLevel(pair, 51.1, 3000, CryptoOrderSide.Ask, null, "ask3"),
                CreateLevel(pair, 80.123, 1234, CryptoOrderSide.Ask, null, "ask4")
            ));

            Assert.Equal(2, orderBookUpdatedCount);
            Assert.Equal(2, topNLevelsUpdatedCount);
            Assert.Equal(49.4, topNLevelsChangeInfo.Snapshot.Bids[0].Price);
            Assert.Equal(75, topNLevelsChangeInfo.Snapshot.Bids[0].Amount);
            Assert.Equal(49, topNLevelsChangeInfo.Snapshot.Bids[1].Price);
            Assert.Equal(49.9, topNLevelsChangeInfo.Snapshot.Asks[0].Price);
            Assert.Equal(600, topNLevelsChangeInfo.Snapshot.Asks[0].Amount);
            Assert.Equal(50, topNLevelsChangeInfo.Snapshot.Asks[1].Price);

            source.StreamBulk(GetInsertBulk(CryptoOrderBookType.L3,
                CreateLevel(pair, 46.5, 50, CryptoOrderSide.Bid, null, "bid5"),
                CreateLevel(pair, 52.5, 50, CryptoOrderSide.Ask, null, "ask5")
            ));

            Assert.Equal(3, orderBookUpdatedCount);
            Assert.Equal(2, topNLevelsUpdatedCount);

            source.StreamBulk(GetUpdateBulk(CryptoOrderBookType.L3,
                CreateLevel(pair, 47, 125, CryptoOrderSide.Bid, null, orderBook.BidLevelsPerPrice[47].First().Id)
            ));

            Assert.Equal(4, orderBookUpdatedCount);
            Assert.Equal(3, topNLevelsUpdatedCount);

            source.StreamBulk(GetUpdateBulk(CryptoOrderBookType.L3,
                CreateLevel(pair, 52, 125, CryptoOrderSide.Ask, null, orderBook.AskLevelsPerPrice[52].First().Id)
            ));

            Assert.Equal(5, orderBookUpdatedCount);
            Assert.Equal(4, topNLevelsUpdatedCount);

            source.StreamBulk(GetUpdateBulk(CryptoOrderBookType.L3,
                CreateLevel(pair, 46.5, 175, CryptoOrderSide.Bid, null, "bid5"),
                CreateLevel(pair, 52.5, 175, CryptoOrderSide.Ask, null, "ask5")
            ));

            Assert.Equal(6, orderBookUpdatedCount);
            Assert.Equal(4, topNLevelsUpdatedCount);
            Assert.Equal(49.4, topNLevelsChangeInfo.Snapshot.Bids[0].Price);
            Assert.Equal(75, topNLevelsChangeInfo.Snapshot.Bids[0].Amount);
            Assert.Equal(49, topNLevelsChangeInfo.Snapshot.Bids[1].Price);
            Assert.Equal(48.3, topNLevelsChangeInfo.Snapshot.Bids[2].Price);
            Assert.Equal(48, topNLevelsChangeInfo.Snapshot.Bids[3].Price);
            Assert.Equal(47, topNLevelsChangeInfo.Snapshot.Bids[4].Price);
            Assert.Equal(49.9, topNLevelsChangeInfo.Snapshot.Asks[0].Price);
            Assert.Equal(600, topNLevelsChangeInfo.Snapshot.Asks[0].Amount);
            Assert.Equal(50, topNLevelsChangeInfo.Snapshot.Asks[1].Price);
            Assert.Equal(51, topNLevelsChangeInfo.Snapshot.Asks[2].Price);
            Assert.Equal(51, 1, topNLevelsChangeInfo.Snapshot.Asks[3].Price);
            Assert.Equal(52, 1, topNLevelsChangeInfo.Snapshot.Asks[3].Price);
        }

        [Fact]
        public void StreamingSnapshot_ShouldHandleCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockDataL3(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3);
            var source = new OrderBookSourceMock(snapshot);

            ICryptoOrderBook orderBook = new CryptoOrderBook(pair, source, CryptoOrderBookType.L3);

            source.StreamSnapshot();

            Assert.Equal(500, orderBook.BidLevels.Length);
            Assert.Equal(500, orderBook.AskLevels.Length);

            Assert.Equal(49, orderBook.BidPrice);
            Assert.Equal(1490, orderBook.BidLevels.First().Amount);

            Assert.Equal(50, orderBook.AskPrice);
            Assert.Equal(2509, orderBook.AskLevels.First().Amount);

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
            var data1 = GetOrderBookSnapshotMockDataL3(pair1, 500);
            var data2 = GetOrderBookSnapshotMockDataL3(pair2, 200);
            var data = data2.Concat(data1).ToArray();
            var now = CryptoDateUtils.ConvertFromUnixSeconds(1577575307.123451);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3)
            {
                ExchangeName = "test",
                ServerSequence = 3,
                ServerTimestamp = now
            };
            var source = new OrderBookSourceMock(snapshot);

            ICryptoOrderBook orderBook1 = new CryptoOrderBook(pair1, source, CryptoOrderBookType.All);
            ICryptoOrderBook orderBook2 = new CryptoOrderBook(pair2, source, CryptoOrderBookType.All);

            orderBook1.OrderBookUpdatedStream.Subscribe(x =>
            {
                Assert.Equal("test", x.ExchangeName);
                Assert.Equal(3, x.ServerSequence);
                Assert.Equal(now, x.ServerTimestamp);
            });

            orderBook2.OrderBookUpdatedStream.Subscribe(x =>
            {
                Assert.Equal("test", x.ExchangeName);
                Assert.Equal(3, x.ServerSequence);
                Assert.Equal(now, x.ServerTimestamp);
            });

            source.StreamSnapshot();

            Assert.Equal(500, orderBook1.BidLevels.Length);
            Assert.Equal(500, orderBook1.AskLevels.Length);

            Assert.Equal(200, orderBook2.BidLevels.Length);
            Assert.Equal(200, orderBook2.AskLevels.Length);

            Assert.Equal(49, orderBook1.BidLevels.First().Price);
            Assert.Equal(1490, orderBook1.BidLevels.First().Amount);

            Assert.Equal(19, orderBook2.BidLevels.First().Price);
            Assert.Equal(590, orderBook2.BidLevels.First().Amount);

            Assert.Equal(50, orderBook1.AskLevels.First().Price);
            Assert.Equal(2509, orderBook1.AskLevels.First().Amount);

            Assert.Equal(20, orderBook2.AskLevels.First().Price);
            Assert.Equal(1009, orderBook2.AskLevels.First().Amount);

            var levels = orderBook1.Levels;
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
            var data1 = GetOrderBookSnapshotMockDataL3(pair1, 500);
            var data2 = GetOrderBookSnapshotMockDataL3(pair2, 200);
            var data = data2.Concat(data1).ToArray();
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3);
            var source = new OrderBookSourceMock(snapshot);

            ICryptoOrderBook orderBook = new CryptoOrderBook(pair1, source);

            source.StreamSnapshot();

            var bids = orderBook.FindBidLevelsByPrice(0);
            var asks = orderBook.FindAskLevelsByPrice(99);

            Assert.Equal(1000, orderBook.FindBidLevelByPrice(0)?.Amount);
            Assert.Equal(10, bids.Length);

            Assert.Equal(3000, orderBook.FindAskLevelByPrice(100)?.Amount);
            Assert.Equal(10, asks.Length);
        }

        [Fact]
        public void UpdateDelete_ShouldConstructCorrectOrderBook()
        {
            var pair1 = "BTC/USD";

            var source = new OrderBookSourceMock();
            source.BufferEnabled = false;

            ICryptoOrderBook orderBook = new CryptoOrderBook(pair1, source);
            orderBook.IgnoreDiffsBeforeSnapshot = false;

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.123, CryptoOrderSide.Ask, null, "ASK1"))
                );

            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.444, CryptoOrderSide.Ask, null, "ASK2"))
            );
            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.555, CryptoOrderSide.Ask, null, "ASK3"))
            );

            var asks = orderBook.FindAskLevelsByPrice(100.111);
            Assert.Equal(3, asks.Length);
            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);

            source.StreamBulk(
                GetDeleteBulk(CryptoOrderBookType.L3, CreateLevel(pair1, null, CryptoOrderSide.Ask, "ASK1"))
            );

            asks = orderBook.FindAskLevelsByPrice(100.111);
            Assert.Equal(2, asks.Length);
            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.444, orderBook.AskAmount);

            source.StreamBulk(
                GetUpdateBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.888, CryptoOrderSide.Ask, null, "ASK2"))
            );

            asks = orderBook.FindAskLevelsByPrice(100.111);
            Assert.Equal(2, asks.Length);
            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.555, orderBook.AskAmount);
        }

        [Fact]
        public void UpdateDeleteComplex_ShouldConstructCorrectOrderBook()
        {
            var pair1 = "BTC/USD";
            var data = GetOrderBookSnapshotMockDataL3(pair1, 200);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L3);

            var source = new OrderBookSourceMock(snapshot);
            source.BufferEnabled = false;

            var orderBook = new CryptoOrderBook(pair1, source);
            orderBook.IgnoreDiffsBeforeSnapshot = true;

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.123, CryptoOrderSide.Ask, null, "ASK1"))
                );

            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.BidAmount);
            Assert.Equal(0, orderBook.AskPrice);
            Assert.Equal(0, orderBook.AskAmount);

            source.StreamSnapshot();

            Assert.Equal(19, orderBook.BidPrice);
            Assert.Equal(590, orderBook.BidAmount);
            Assert.Equal(20, orderBook.AskPrice);
            Assert.Equal(1009, orderBook.AskAmount);

            var asks = orderBook.FindAskLevelsByPrice(20);
            var levelsToDelete = asks
                .Select(x => CreateLevel(x.Pair, null, x.Side, x.Id))
                .ToArray();

            source.StreamBulk(
                GetDeleteBulk(CryptoOrderBookType.L3, levelsToDelete)
            );

            source.StreamBulk(
                GetUpdateBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 20, 0.123, CryptoOrderSide.Bid, null, "BID1"))
            );

            Assert.Equal(20, orderBook.BidPrice);
            Assert.Equal(0.123, orderBook.BidAmount);
            Assert.Equal(21, orderBook.AskPrice);
            Assert.Equal(1019, orderBook.AskAmount);

            var bids = orderBook.BidLevelsPerPrice;

            foreach (var bidLevel in bids[19])
            {
                source.StreamBulk(
                    GetDeleteBulk(CryptoOrderBookType.L3, CreateLevel(pair1, bidLevel.Price, bidLevel.Amount, bidLevel.Side, null, bidLevel.Id))
                );
            }

            source.StreamBulk(
                GetDeleteBulk(CryptoOrderBookType.L3, CreateLevel(pair1, null, CryptoOrderSide.Bid, "BID1"))
            );

            Assert.Equal(18, orderBook.BidPrice);
            Assert.Equal(580, orderBook.BidAmount);
            Assert.Equal(21, orderBook.AskPrice);
            Assert.Equal(1019, orderBook.AskAmount);
        }

        [Fact]
        public void UpdatePrice_ShouldConstructCorrectOrderBook()
        {
            var pair1 = "BTC/USD";

            var source = new OrderBookSourceMock();
            source.BufferEnabled = false;

            var orderBook = new CryptoOrderBook(pair1, source);
            orderBook.IgnoreDiffsBeforeSnapshot = false;

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.123, CryptoOrderSide.Ask, null, "ASK1"))
            );
            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.444, CryptoOrderSide.Ask, null, "ASK2"))
            );
            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 101, 0.555, CryptoOrderSide.Ask, null, "ASK3"))
            );
            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 101, 0.666, CryptoOrderSide.Ask, null, "ASK4"))
            );

            var asks = orderBook.FindAskLevelsByPrice(100.111);
            Assert.Equal(2, asks.Length);
            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);

            source.StreamBulk(
                GetUpdateBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100, 0.555, CryptoOrderSide.Ask, null, "ASK3"))
            );
            source.StreamBulk(
                GetUpdateBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 101, 0.123, CryptoOrderSide.Ask, null, "ASK1"))
            );

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.111222, CryptoOrderSide.Bid, null, "BID1"))
            );

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 99, 0.9, CryptoOrderSide.Bid, null, "BID2"))
            );

            var allAsks = orderBook.AskLevelsPerPrice;
            Assert.Equal(100.111, orderBook.BidPrice);
            Assert.Equal(0.111222, orderBook.BidAmount);
            Assert.Equal(100, orderBook.AskPrice);
            Assert.Equal(0.555, orderBook.AskAmount);
            Assert.Equal(3, allAsks.Count);
            Assert.Single(allAsks[100]);
            Assert.Equal(2, allAsks[101].Length);
            Assert.Single(allAsks[100.111]);

            source.StreamBulk(
                GetUpdateBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 101, 0.123123, CryptoOrderSide.Bid, null, "BID1"))
            );

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 101, 0.777, CryptoOrderSide.Ask, null, "ASK5"))
            );

            var ask101 = orderBook.FindAskLevelsByPrice(101);
            Assert.Equal(3, ask101.Length);
            Assert.Equal("ASK4", ask101[0].Id);
            Assert.Equal("ASK1", ask101[1].Id);
            Assert.Equal("ASK5", ask101[2].Id);

            source.StreamBulk(
                GetDeleteBulk(CryptoOrderBookType.L3, CreateLevel(pair1, null, CryptoOrderSide.Ask, "ASK4"))
            );
            source.StreamBulk(
                GetDeleteBulk(CryptoOrderBookType.L3, CreateLevel(pair1, null, CryptoOrderSide.Bid, "BID2"))
            );
            source.StreamBulk(
                GetDeleteBulk(CryptoOrderBookType.L3, CreateLevel(pair1, null, CryptoOrderSide.Ask, "ASK3"))
            );
            source.StreamBulk(
                GetUpdateBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.123, CryptoOrderSide.Ask, null, "ASK1"))
            );

            allAsks = orderBook.AskLevelsPerPrice;
            Assert.Equal(101, orderBook.BidPrice);
            Assert.Equal(0.123123, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.444, orderBook.AskAmount);
            Assert.Equal(2, allAsks.Count);
            Assert.Equal(2, allAsks[100.111].Length);
            Assert.Equal("ASK5", allAsks[101][0].Id);

        }

        [Fact]
        public void NegativePrice_ShouldHandleCorrectly()
        {
            var pair1 = "BTC/USD";

            var source = new OrderBookSourceMock();
            source.BufferEnabled = false;

            var orderBook = new CryptoOrderBook(pair1, source);
            orderBook.IgnoreDiffsBeforeSnapshot = false;

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.123, CryptoOrderSide.Ask, null, "ASK1"))
                );

            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, -200, 0.444, CryptoOrderSide.Bid, null, "BID1"))
            );
            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, -100, 0.555, CryptoOrderSide.Bid, null, "BID2"))
            );

            Assert.Equal(-100, orderBook.BidPrice);
            Assert.Equal(0.555, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100, 777, CryptoOrderSide.Bid, null, "BID2"))
            );

            Assert.Equal(100, orderBook.BidPrice);
            Assert.Equal(777, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);
        }

        [Fact]
        public void InvalidBidAsk_ShouldBePreserved()
        {
            var pair1 = "BTC/USD";

            var source = new OrderBookSourceMock();
            source.BufferEnabled = false;

            ICryptoOrderBook orderBook = new CryptoOrderBook(pair1, source);
            orderBook.IgnoreDiffsBeforeSnapshot = false;

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100.111, 0.123, CryptoOrderSide.Ask, null, "ASK1"))
            );

            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);

            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 100, 0.444, CryptoOrderSide.Bid, null, "BID1"))
            );
            source.StreamBulk(
                GetInsertBulk(CryptoOrderBookType.L3, CreateLevel(pair1, 103, 0.555, CryptoOrderSide.Bid, null, "BID2"))
            );

            Assert.Equal(103, orderBook.BidPrice);
            Assert.Equal(0.555, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);

            source.StreamBulk(
                GetDeleteBulk(CryptoOrderBookType.L3, CreateLevel(pair1, null, CryptoOrderSide.Bid, "BID2"))
            );

            Assert.Equal(100, orderBook.BidPrice);
            Assert.Equal(0.444, orderBook.BidAmount);
            Assert.Equal(100.111, orderBook.AskPrice);
            Assert.Equal(0.123, orderBook.AskAmount);
        }
    }
}
