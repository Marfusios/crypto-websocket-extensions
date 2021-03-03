using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bitmex.Client.Websocket.Client;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Tests.data;
using Crypto.Websocket.Extensions.Tests.Helpers;
using Xunit;

using static Crypto.Websocket.Extensions.Tests.Helpers.OrderBookTestUtils;

namespace Crypto.Websocket.Extensions.Tests
{
    public class CryptoOrderBookL2Tests
    {
        private readonly string[] _rawFiles = {
            "data/bitmex_raw_xbtusd_2018-11-13.txt.gz"
        };

        [Fact]
        public void StreamingSnapshot_ShouldHandleCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);

            var orderBook = new CryptoOrderBookL2(pair, source);

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
            var now = CryptoDateUtils.ConvertFromUnixSeconds(1577575307.123451);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2)
            {
                ExchangeName = "test",
                ServerSequence = 3,
                ServerTimestamp = now
            };
            var source = new OrderBookSourceMock(snapshot);

            var orderBook1 = new CryptoOrderBookL2(pair1, source);
            var orderBook2 = new CryptoOrderBookL2(pair2, source);

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

            Assert.Equal(499, orderBook1.BidLevels.First().Price);
            Assert.Equal(1499, orderBook1.BidLevels.First().Amount);

            Assert.Equal(199, orderBook2.BidLevels.First().Price);
            Assert.Equal(599, orderBook2.BidLevels.First().Amount);

            Assert.Equal(501, orderBook1.AskLevels.First().Price);
            Assert.Equal(2501, orderBook1.AskLevels.First().Amount);

            Assert.Equal(201, orderBook2.AskLevels.First().Price);
            Assert.Equal(1001, orderBook2.AskLevels.First().Amount);
            
            var levels = orderBook1.Levels;
            foreach (var level in levels)
            {
                Assert.Equal(CryptoPairsHelper.Clean(pair1), level.Pair);
            }
        }

        [Fact]
        public void StreamingSnapshot_DifferentPairsSeparately_ShouldHandleCorrectly()
        {
            var pair1 = "BTC/USD";
            var pair2 = "ETH/BTC";
            var data1 = GetOrderBookSnapshotMockData(pair1, 500);
            var data2 = GetOrderBookSnapshotMockData(pair2, 200);
            var now = CryptoDateUtils.ConvertFromUnixSeconds(1577575307.123451);

            var snapshot1 = new OrderBookLevelBulk(OrderBookAction.Insert, data1, CryptoOrderBookType.L2)
            {
                ExchangeName = "test",
                ServerSequence = 4,
                ServerTimestamp = now.AddMilliseconds(1)
            };
            var snapshot2 = new OrderBookLevelBulk(OrderBookAction.Insert, data2, CryptoOrderBookType.L2)
            {
                ExchangeName = "test",
                ServerSequence = 5,
                ServerTimestamp = now.AddMilliseconds(2)
            };

            var source = new OrderBookSourceMock();

            var orderBook1 = new CryptoOrderBookL2(pair1, source);
            var orderBook2 = new CryptoOrderBookL2(pair2, source);

            orderBook1.OrderBookUpdatedStream.Subscribe(x =>
            {
                Assert.Equal("test", x.ExchangeName);
                Assert.Equal(4, x.ServerSequence);
                Assert.Equal("1577575307.124451", x.ServerTimestamp.ToUnixSecondsString());
            });

            orderBook2.OrderBookUpdatedStream.Subscribe(x =>
            {
                Assert.Equal("test", x.ExchangeName);
                Assert.Equal(5, x.ServerSequence);
                Assert.Equal("1577575307.125451", x.ServerTimestamp.ToUnixSecondsString());
            });

            source.StreamSnapshotRaw(snapshot1);
            source.StreamSnapshotRaw(snapshot2);

            Assert.Equal(500, orderBook1.BidLevels.Length);
            Assert.Equal(500, orderBook1.AskLevels.Length);

            Assert.Equal(200, orderBook2.BidLevels.Length);
            Assert.Equal(200, orderBook2.AskLevels.Length);

            Assert.Equal(499, orderBook1.BidLevels.First().Price);
            Assert.Equal(1499, orderBook1.BidLevels.First().Amount);

            Assert.Equal(199, orderBook2.BidLevels.First().Price);
            Assert.Equal(599, orderBook2.BidLevels.First().Amount);

            Assert.Equal(501, orderBook1.AskLevels.First().Price);
            Assert.Equal(2501, orderBook1.AskLevels.First().Amount);

            Assert.Equal(201, orderBook2.AskLevels.First().Price);
            Assert.Equal(1001, orderBook2.AskLevels.First().Amount);

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
            var data1 = GetOrderBookSnapshotMockData(pair1, 500);
            var data2 = GetOrderBookSnapshotMockData(pair2, 200);
            var data = data2.Concat(data1).ToArray();
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);

            var orderBook = new CryptoOrderBookL2(pair1, source);

            source.StreamSnapshot();

            Assert.Equal(1000, orderBook.FindBidLevelByPrice(0)?.Amount);
            Assert.Equal(1000, orderBook.FindBidLevelById("0-bid")?.Amount);

            Assert.Equal(3000, orderBook.FindAskLevelByPrice(1000)?.Amount);
            Assert.Equal(3000, orderBook.FindAskLevelById("1000-ask")?.Amount);
        }

        [Fact]
        public async Task StreamingDiff_BeforeSnapshot_ShouldDoNothing()
        {
            var pair = "BTC/USD";
            var source = new OrderBookSourceMock();
            var orderBook = new CryptoOrderBookL2(pair, source);

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 100, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 55, 600, CryptoOrderSide.Bid),
                CreateLevel(pair, 105, 400, CryptoOrderSide.Ask),
                CreateLevel(pair, 200, 3000, CryptoOrderSide.Ask)
            ));

            await Task.Delay(500);

            Assert.Empty(orderBook.BidLevels);
            Assert.Empty(orderBook.AskLevels);

            Assert.Equal(0, orderBook.BidPrice);
            Assert.Equal(0, orderBook.AskPrice);
        }


#if DEBUG

        [Fact]
        public async Task StreamingDiff_ShouldHandleCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);

            var orderBook = new CryptoOrderBookL2(pair, source);

            source.StreamSnapshot();

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 499.4, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 498.3, 600, CryptoOrderSide.Bid),
                CreateLevel(pair, 300.33, 3350, CryptoOrderSide.Bid),
                CreateLevel(pair, 500.2, 400, CryptoOrderSide.Ask),
                CreateLevel(pair, 503.1, 3000, CryptoOrderSide.Ask),
                CreateLevel(pair, 800.123, 1234, CryptoOrderSide.Ask),

                CreateLevel(null, 101.1, null, CryptoOrderSide.Bid),
                CreateLevel(null, 901.1, null, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 499, 33, CryptoOrderSide.Bid),
                CreateLevel(pair, 450, 33, CryptoOrderSide.Bid),
                CreateLevel(pair, 501, 32, CryptoOrderSide.Ask),
                CreateLevel(pair, 503.1, 32, CryptoOrderSide.Ask),

                CreateLevel(pair, 100, null, CryptoOrderSide.Bid),
                CreateLevel(pair, 900, null, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair, 0, CryptoOrderSide.Bid),
                CreateLevel(pair, 1, CryptoOrderSide.Bid),
                CreateLevel(pair, 1000, CryptoOrderSide.Ask),
                CreateLevel(pair, 999, CryptoOrderSide.Ask)
            ));

            await Task.Delay(500);

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

            Assert.Null(orderBook.FindBidLevelByPrice(101.1));
            Assert.Null(orderBook.FindAskLevelByPrice(901.1));
        }

        [Fact]
        public async Task StreamingDiff_TwoPairs_ShouldHandleCorrectly()
        {
            var pair1 = "BTC/USD";
            var pair2 = "ETH/USD";

            var data1 = GetOrderBookSnapshotMockData(pair1, 500);
            var data2 = GetOrderBookSnapshotMockData(pair2, 200);
            var data = data2.Concat(data1).ToArray();
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);

            var orderBook1 = new CryptoOrderBookL2(pair1, source) {DebugEnabled = true};
            var orderBook2 = new CryptoOrderBookL2(pair2, source) {DebugEnabled = true};

            source.StreamSnapshot();

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair2, 199.4, 50, CryptoOrderSide.Bid),
                CreateLevel(pair2, 198.3, 600, CryptoOrderSide.Bid),
                CreateLevel(pair2, 50.33, 3350, CryptoOrderSide.Bid),

                CreateLevel(pair1, 500.2, 400, CryptoOrderSide.Ask),
                CreateLevel(pair1, 503.1, 3000, CryptoOrderSide.Ask),
                CreateLevel(pair1, 800.123, 1234, CryptoOrderSide.Ask),

                CreateLevel(null, 101.1, null, CryptoOrderSide.Bid),
                CreateLevel(null, 901.1, null, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair1, 499.4, 50, CryptoOrderSide.Bid),
                CreateLevel(pair1, 498.3, 600, CryptoOrderSide.Bid),
                CreateLevel(pair1, 300.33, 3350, CryptoOrderSide.Bid),

                CreateLevel(pair2, 200.2, 400, CryptoOrderSide.Ask),
                CreateLevel(pair2, 203.1, 3000, CryptoOrderSide.Ask),
                CreateLevel(pair2, 250.123, 1234, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair1, 499, 33, CryptoOrderSide.Bid),
                CreateLevel(pair1, 450, 33, CryptoOrderSide.Bid),
                CreateLevel(pair1, 501, 32, CryptoOrderSide.Ask),
                CreateLevel(pair1, 503.1, 32, CryptoOrderSide.Ask),

                CreateLevel(pair1, 100, null, CryptoOrderSide.Bid),
                CreateLevel(pair1, 900, null, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair1, 0, CryptoOrderSide.Bid),
                CreateLevel(pair1, 1, CryptoOrderSide.Bid),

                CreateLevel(pair2, 0, CryptoOrderSide.Bid),
                CreateLevel(pair2, 1, CryptoOrderSide.Bid)
            ));

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair2, 400, CryptoOrderSide.Ask),
                CreateLevel(pair2, 399, CryptoOrderSide.Ask),

                CreateLevel(pair1, 1000, CryptoOrderSide.Ask),
                CreateLevel(pair1, 999, CryptoOrderSide.Ask)
            ));

            await Task.Delay(500);

            Assert.NotEmpty(orderBook1.BidLevels);
            Assert.NotEmpty(orderBook1.AskLevels);

            Assert.Equal(501, orderBook1.BidLevels.Length);
            Assert.Equal(501, orderBook1.AskLevels.Length);

            Assert.Equal(201, orderBook2.BidLevels.Length);
            Assert.Equal(201, orderBook2.AskLevels.Length);

            Assert.Equal(499.4, orderBook1.BidPrice);
            Assert.Equal(500.2, orderBook1.AskPrice);

            Assert.Equal(199.4, orderBook2.BidPrice);
            Assert.Equal(200.2, orderBook2.AskPrice);

            Assert.Equal(33, orderBook1.FindBidLevelByPrice(499)?.Amount);
            Assert.Equal(33, orderBook1.FindBidLevelByPrice(450)?.Amount);

            Assert.Equal(32, orderBook1.FindAskLevelByPrice(501)?.Amount);
            Assert.Equal(32, orderBook1.FindAskLevelByPrice(503.1)?.Amount);

            var notCompleteBid = orderBook1.FindBidLevelByPrice(100);
            Assert.Equal(CryptoPairsHelper.Clean(pair1), notCompleteBid.Pair);
            Assert.Equal(1100, notCompleteBid.Amount);
            Assert.Equal(3, notCompleteBid.Count);

            var notCompleteAsk = orderBook1.FindAskLevelByPrice(900);
            Assert.Equal(CryptoPairsHelper.Clean(pair1), notCompleteAsk.Pair);
            Assert.Equal(2900, notCompleteAsk.Amount);
            Assert.Equal(3, notCompleteAsk.Count);

            Assert.Null(orderBook1.FindBidLevelByPrice(0));
            Assert.Null(orderBook1.FindBidLevelByPrice(1));
            Assert.Null(orderBook1.FindAskLevelByPrice(1000));
            Assert.Null(orderBook1.FindAskLevelByPrice(999));

            Assert.Null(orderBook1.FindBidLevelByPrice(101.1));
            Assert.Null(orderBook1.FindAskLevelByPrice(901.1));

            Assert.Null(orderBook2.FindBidLevelByPrice(101.1));
            Assert.Null(orderBook2.FindAskLevelByPrice(901.1));
        }


        [Fact]
        public async Task StreamingData_ShouldNotifyCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);

            var notificationCount = 0;
            var notificationBidAskCount = 0;
            var notificationTopLevelCount = 0;

            var amountDifferenceBid = 0.0;
            var amountDifferenceAsk = 0.0;

            var changes = new List<IOrderBookChangeInfo>();

            var orderBook = new CryptoOrderBookL2(pair, source) {DebugEnabled = true};

            orderBook.OrderBookUpdatedStream.Subscribe(x =>
            {
                notificationCount++;
                changes.Add(x);
                foreach (var bulk in x.Sources)
                {
                    amountDifferenceBid += bulk.Levels.Where(x => x.Side == CryptoOrderSide.Bid).Sum(x => x.AmountDifference);
                    amountDifferenceAsk += bulk.Levels.Where(x => x.Side == CryptoOrderSide.Ask).Sum(x => x.AmountDifference);
                }
            });
            orderBook.BidAskUpdatedStream.Subscribe(_ => notificationBidAskCount++);
            orderBook.TopLevelUpdatedStream.Subscribe(_ => notificationTopLevelCount++);

            source.StreamSnapshot();

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 499.4, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 500.2, 400, CryptoOrderSide.Ask)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 499.5, 600, CryptoOrderSide.Bid),
                CreateLevel(pair, 300.33, 3350, CryptoOrderSide.Bid)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 503.1, 3000, CryptoOrderSide.Ask),
                CreateLevel(pair, 800.123, 1234, CryptoOrderSide.Ask)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 499, 33, CryptoOrderSide.Bid),
                CreateLevel(pair, 450, 33, CryptoOrderSide.Bid),
                CreateLevel(pair, 501, 32, CryptoOrderSide.Ask),
                CreateLevel(pair, 503.1, 32, CryptoOrderSide.Ask),

                CreateLevel(pair, 100, null, CryptoOrderSide.Bid),
                CreateLevel(pair, 900, null, CryptoOrderSide.Ask)
            ));

            await Task.Delay(50);
            
            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 499.5, 100, CryptoOrderSide.Bid)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 499.5, 200, CryptoOrderSide.Bid)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 500.2, 22, CryptoOrderSide.Ask)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair, 0, CryptoOrderSide.Bid),
                CreateLevel(pair, 1, CryptoOrderSide.Bid),
                CreateLevel(pair, 1000, CryptoOrderSide.Ask),
                CreateLevel(pair, 999, CryptoOrderSide.Ask)
            ));

            await Task.Delay(50);

            Assert.Equal(9, notificationCount);
            Assert.Equal(3, notificationBidAskCount);
            Assert.Equal(6, notificationTopLevelCount);

            var firstChange = changes.First();
            var secondChange = changes[1];

            Assert.Equal(0, firstChange.Levels.First().Price);
            Assert.Equal(501, firstChange.Levels.Last().Price);

            Assert.Equal(499.4, secondChange.Levels.First().Price);
            Assert.Equal(500.2, secondChange.Levels.Last().Price);

            Assert.Equal(623466, amountDifferenceBid);
            Assert.Equal(1368070, amountDifferenceAsk);
        }


        [Fact]
        public async Task StreamingData_ShouldNotifyOneByOne()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            source.BufferInterval = TimeSpan.FromMilliseconds(10);

            var notificationCount = 0;

            var changes = new List<IOrderBookChangeInfo>();

            var orderBook = new CryptoOrderBookL2(pair, source) {DebugEnabled = true};

            orderBook.OrderBookUpdatedStream.Subscribe(x =>
            {
                notificationCount++;
                changes.Add(x);
                Thread.Sleep(2000);
            });

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 499.4, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 500.2, 400, CryptoOrderSide.Ask)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 499.5, 600, CryptoOrderSide.Bid),
                CreateLevel(pair, 300.33, 3350, CryptoOrderSide.Bid)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 503.1, 3000, CryptoOrderSide.Ask),
                CreateLevel(pair, 800.123, 1234, CryptoOrderSide.Ask)
            ));

            await Task.Delay(50);

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 504.1, 3000, CryptoOrderSide.Ask),
                CreateLevel(pair, 800.101, 1234, CryptoOrderSide.Ask)
            ));

            await Task.Delay(50);

            Assert.Equal(1, notificationCount);

            await Task.Delay(2100);

            Assert.Equal(2, notificationCount);

            var firstChange = changes.First();
            var secondChange = changes[1];

            Assert.Equal(2, firstChange.Levels.Length);
            Assert.Equal(499.4, firstChange.Levels.First().Price);
            Assert.Equal(500.2, firstChange.Levels.Last().Price);

            Assert.Equal(6, secondChange.Levels.Length);
        }

        [Fact]
        public async Task AutoSnapshotReloading_ShouldWorkCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot)
            {
                LoadSnapshotEnabled = true,
                BufferInterval = TimeSpan.FromMilliseconds(100)
            };

            var orderBook = new CryptoOrderBookL2(pair, source)
            {
                SnapshotReloadTimeout = TimeSpan.FromMilliseconds(500), 
                SnapshotReloadEnabled = true
            };

            await Task.Delay(TimeSpan.FromSeconds(6));

            Assert.Equal(pair, source.SnapshotLastPair);
            Assert.True(source.SnapshotCalledCount >= 4);
        }


        [Fact]
        public async Task ValidityChecking_ShouldWorkCorrectly()
        {
            var pair = "BTC/USD";
            var data = new []
            {
                CreateLevel(pair, 480, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 520, 50, CryptoOrderSide.Ask),
            };
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            source.BufferInterval = TimeSpan.FromMilliseconds(100);
            var orderBookUpdatedCount = 0;

            var orderBook = new CryptoOrderBookL2(pair, source)
            {
                ValidityCheckTimeout = TimeSpan.FromMilliseconds(200), 
                ValidityCheckEnabled = true,
                ValidityCheckLimit = 2
            };

            orderBook.OrderBookUpdatedStream.Subscribe(x =>
            {
                orderBookUpdatedCount++;
            });

            source.StreamSnapshot();
            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 500, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 499, 400, CryptoOrderSide.Ask)
            ));

            await Task.Delay(TimeSpan.FromMilliseconds(2000));

            Assert.Equal(pair, source.SnapshotLastPair);
            Assert.InRange(source.SnapshotCalledCount, 4, 5);
            Assert.Equal(2, orderBookUpdatedCount);
        }

        [Fact]
        public async Task ValidityChecking_Disabling_ShouldWork()
        {
            var pair = "BTC/USD";
            var data = new []
            {
                CreateLevel(pair, 480, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 520, 50, CryptoOrderSide.Ask),
            };
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            var orderBookUpdatedCount = 0;

            var orderBook = new CryptoOrderBookL2(pair, source)
            {
                ValidityCheckTimeout = TimeSpan.FromMilliseconds(200), 
                ValidityCheckEnabled = false
            };

            orderBook.OrderBookUpdatedStream.Subscribe(x =>
            {
                orderBookUpdatedCount++;
            });

            source.StreamSnapshot();
            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 500, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 499, 400, CryptoOrderSide.Ask)
            ));

            await Task.Delay(TimeSpan.FromMilliseconds(500));

            Assert.Null(source.SnapshotLastPair);
            Assert.Equal(0, source.SnapshotCalledCount);
            Assert.Equal(2, orderBookUpdatedCount);
        }
#endif

        [Fact]
        public void NegativePrice_ShouldHandleCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            source.BufferEnabled = false;

            var orderBook = new CryptoOrderBookL2(pair, source) {DebugEnabled = true};
            orderBook.IgnoreDiffsBeforeSnapshot = false;

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, -200, -50, CryptoOrderSide.Bid),
                CreateLevel(pair, 500, -400, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, -50, -600, CryptoOrderSide.Bid),
                CreateLevel(pair, -100, -3350, CryptoOrderSide.Bid)
            ));

            Assert.Equal(-50, orderBook.BidPrice);
            Assert.Equal(600, orderBook.BidAmount);

            Assert.Equal(500, orderBook.AskPrice);
            Assert.Equal(400, orderBook.AskAmount);
        }

        [Fact]
        public void InvalidBidAsk_ShouldBePreserved()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            source.BufferEnabled = false;

            var orderBook = new CryptoOrderBookL2(pair, source) {DebugEnabled = true};
            orderBook.IgnoreDiffsBeforeSnapshot = false;

            var changesTopLevel = new List<IOrderBookChangeInfo>();
            orderBook.BidAskUpdatedStream.Subscribe(x => changesTopLevel.Add(x));

            var changes = new List<IOrderBookChangeInfo>();
            orderBook.OrderBookUpdatedStream.Subscribe(x => changes.Add(x));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 100, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 200, 400, CryptoOrderSide.Ask),
                CreateLevel(pair, 300, 600, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 205, 60, CryptoOrderSide.Bid),
                CreateLevel(pair, 211, 333, CryptoOrderSide.Bid)
            ));

            Assert.Equal(211, orderBook.BidPrice);
            Assert.Equal(333, orderBook.BidAmount);

            Assert.Equal(200, orderBook.AskPrice);
            Assert.Equal(400, orderBook.AskAmount);

            Assert.False(orderBook.IsValid());

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair, 200, CryptoOrderSide.Ask)
            ));

            Assert.Equal(211, orderBook.BidPrice);
            Assert.Equal(333, orderBook.BidAmount);

            Assert.Equal(300, orderBook.AskPrice);
            Assert.Equal(600, orderBook.AskAmount);

            Assert.True(orderBook.IsValid());

            Assert.Equal(3, changesTopLevel.Count);
            Assert.True(changesTopLevel[0].Quotes.IsValid());
            Assert.False(changesTopLevel[1].Quotes.IsValid());
            Assert.True(changesTopLevel[2].Quotes.IsValid());

            Assert.Equal(3, changes.Count);
            Assert.True(changes[0].Quotes.IsValid());
            Assert.False(changes[1].Quotes.IsValid());
            Assert.True(changes[2].Quotes.IsValid());
        }

        [Fact]
        public void DifferentPairs_ShouldNotNotifyAboutOtherPair()
        {
            var pair1 = "BTC/USD";
            var pair2 = "ETH/USD";

            var ob1NotifiedCount = 0;
            var ob2NotifiedCount = 0;

            var data1 = GetOrderBookSnapshotMockData(pair1, 500);

            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data1, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            source.BufferEnabled = false;

            ICryptoOrderBook orderBook1 = new CryptoOrderBookL2(pair1, source) {DebugEnabled = true};
            ICryptoOrderBook orderBook2 = new CryptoOrderBookL2(pair2, source) {DebugEnabled = true};

            orderBook1.IgnoreDiffsBeforeSnapshot = false;
            orderBook2.IgnoreDiffsBeforeSnapshot = false;

            orderBook1.OrderBookUpdatedStream.Subscribe(x => ob1NotifiedCount++);
            orderBook1.TopLevelUpdatedStream.Subscribe(x => ob1NotifiedCount++);
            orderBook1.BidAskUpdatedStream.Subscribe(x => ob1NotifiedCount++);

            orderBook2.OrderBookUpdatedStream.Subscribe(x => ob2NotifiedCount++);
            orderBook2.TopLevelUpdatedStream.Subscribe(x => ob2NotifiedCount++);
            orderBook2.BidAskUpdatedStream.Subscribe(x => ob2NotifiedCount++);

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair1, -200, -50, CryptoOrderSide.Bid),
                CreateLevel(pair1, 500, -400, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair1, -50, -600, CryptoOrderSide.Bid),
                CreateLevel(pair1, -100, -3350, CryptoOrderSide.Bid)
            ));

            Assert.Equal(-50, orderBook1.BidPrice);
            Assert.Equal(600, orderBook1.BidAmount);

            Assert.Equal(500, orderBook1.AskPrice);
            Assert.Equal(400, orderBook1.AskAmount);

            Assert.Equal(0, ob2NotifiedCount);
            Assert.Equal(2*3, ob1NotifiedCount);
        }

        [Fact]
        public async Task StreamingFromFile_ShouldHandleCorrectly()
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

            var receivedUpdate = 0;
            IOrderBookChangeInfo lastReceivedUpdate = null;

            var receivedTopLevel = 0;
            IOrderBookChangeInfo lastReceivedTopLevel = null;

            var receivedBidAskUpdate = 0;
            IOrderBookChangeInfo lastReceivedBidAskUpdate = null;


            orderBook.OrderBookUpdatedStream.Subscribe(x =>
            {
                receivedUpdate++;
                lastReceivedUpdate = x;
            });
            orderBook.TopLevelUpdatedStream.Subscribe(x =>
            {
                receivedTopLevel++;
                lastReceivedTopLevel = x;
            });
            orderBook.BidAskUpdatedStream.Subscribe(x =>
            {
                receivedBidAskUpdate++;
                lastReceivedBidAskUpdate = x;
            });


            await communicator.Start();

            Assert.Equal(3284, orderBook.BidLevels.Length);
            Assert.Equal(4035, orderBook.AskLevels.Length);

            Assert.Equal(6249, orderBook.BidPrice);
            Assert.Equal(163556, orderBook.BidLevels.First().Amount);

            Assert.Equal(6249.5, orderBook.AskPrice);
            Assert.Equal(270526, orderBook.AskLevels.First().Amount);

            Assert.Equal(322703, receivedUpdate);
            Assert.Equal(6249, lastReceivedUpdate?.Quotes?.Bid);
            Assert.Equal(6249.5, lastReceivedUpdate?.Quotes?.Ask);
            Assert.Equal("8799374800", lastReceivedUpdate.Sources[0].Levels[0].Id);
            Assert.Equal(6252, lastReceivedUpdate.Sources[0].Levels[0].Price);

            Assert.Equal(79020, receivedTopLevel);
            Assert.Equal(6249, lastReceivedTopLevel?.Quotes?.Bid);
            Assert.Equal(6249.5, lastReceivedTopLevel?.Quotes?.Ask);
            Assert.Equal("8799375100", lastReceivedTopLevel.Sources[0].Levels[0].Id);
            Assert.Equal(6249, lastReceivedTopLevel.Sources[0].Levels[0].Price);

            Assert.Equal(584, receivedBidAskUpdate);
            Assert.Equal(6249, lastReceivedBidAskUpdate?.Quotes?.Bid);
            Assert.Equal(6249.5, lastReceivedBidAskUpdate?.Quotes?.Ask);
            Assert.Equal("8799375050", lastReceivedBidAskUpdate.Sources[0].Levels[0].Id);
            Assert.Equal(6249.5, lastReceivedBidAskUpdate.Sources[0].Levels[0].Price);

            var levels = orderBook.Levels;
            foreach (var level in levels)
            {
                Assert.Equal(CryptoPairsHelper.Clean(pair), level.Pair);
            }
        }

        [Fact]
        public void StreamingData_ShouldComputeDifferenceCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            source.BufferEnabled = false;

            var amountDifferenceBid = 0.0;
            var amountDifferenceAsk = 0.0;

            ICryptoOrderBook orderBook = new CryptoOrderBookL2(pair, source) {DebugEnabled = true};
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            orderBook.IgnoreDiffsBeforeSnapshot = false;
            orderBook.IsIndexComputationEnabled = true;

            orderBook.OrderBookUpdatedStream.Subscribe(x =>
            {
                foreach (var bulk in x.Sources)
                {
                    var bidLevels = bulk.Levels.Where(x => x.Side == CryptoOrderSide.Bid).ToArray();
                    var askLevels = bulk.Levels.Where(x => x.Side == CryptoOrderSide.Ask).ToArray();

                    amountDifferenceBid += bidLevels.Sum(x => x.AmountDifference);
                    amountDifferenceAsk += askLevels.Sum(x => x.AmountDifference);
                }
            });

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 100, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 101, 500, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 99, 10, CryptoOrderSide.Bid),
                CreateLevel(pair, 98, 20, CryptoOrderSide.Bid)
            ));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 102, 100, CryptoOrderSide.Ask),
                CreateLevel(pair, 103, 200, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 100, 25, CryptoOrderSide.Bid),
                CreateLevel(pair, 99, 5, CryptoOrderSide.Bid),
                CreateLevel(pair, 101, 250, CryptoOrderSide.Ask),
                CreateLevel(pair, 103, 100, CryptoOrderSide.Ask),

                CreateLevel(pair, 100, null, CryptoOrderSide.Bid),
                CreateLevel(pair, 102, null, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 98, 10, CryptoOrderSide.Bid)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 99, 10, CryptoOrderSide.Bid)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 103, 400, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair, 98, CryptoOrderSide.Bid),
                CreateLevel(pair, 102, CryptoOrderSide.Ask),
                CreateLevel(pair, 103, CryptoOrderSide.Ask)
            ));

            Assert.Equal(35, amountDifferenceBid);
            Assert.Equal(250, amountDifferenceAsk);
        }

        [Fact]
        public void StreamingData_ShouldComputeIndexesCorrectly()
        {
            var pair = "BTC/USD";
            var data = GetOrderBookSnapshotMockData(pair, 500);
            var snapshot = new OrderBookLevelBulk(OrderBookAction.Insert, data, CryptoOrderBookType.L2);
            var source = new OrderBookSourceMock(snapshot);
            source.BufferEnabled = false;

            ICryptoOrderBook orderBook = new CryptoOrderBookL2(pair, source) {DebugEnabled = true};
            orderBook.SnapshotReloadEnabled = false;
            orderBook.ValidityCheckEnabled = false;
            orderBook.IgnoreDiffsBeforeSnapshot = false;
            orderBook.IsIndexComputationEnabled = true;

            var updatedBidLevels = new List<OrderBookLevel>();
            var updatedAskLevels = new List<OrderBookLevel>();

            orderBook.OrderBookUpdatedStream.Subscribe(x =>
            {
                foreach (var bulk in x.Sources)
                {
                    var bidLevels = bulk.Levels.Where(x => x.Side == CryptoOrderSide.Bid).ToArray();
                    var askLevels = bulk.Levels.Where(x => x.Side == CryptoOrderSide.Ask).ToArray();

                    updatedBidLevels.AddRange(bidLevels);
                    updatedAskLevels.AddRange(askLevels);
                }
            });

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 100, 50, CryptoOrderSide.Bid),
                CreateLevel(pair, 101, 500, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 99, 10, CryptoOrderSide.Bid),
                CreateLevel(pair, 98, 20, CryptoOrderSide.Bid)
            ));

            source.StreamBulk(GetInsertBulkL2(
                CreateLevel(pair, 102, 100, CryptoOrderSide.Ask),
                CreateLevel(pair, 103, 200, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 100, 25, CryptoOrderSide.Bid),
                CreateLevel(pair, 99, 5, CryptoOrderSide.Bid),
                CreateLevel(pair, 101, 250, CryptoOrderSide.Ask),
                CreateLevel(pair, 103, 100, CryptoOrderSide.Ask),

                CreateLevel(pair, 100, null, CryptoOrderSide.Bid),
                CreateLevel(pair, 102, null, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 98, 10, CryptoOrderSide.Bid)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 99, 10, CryptoOrderSide.Bid)
            ));

            source.StreamBulk(GetUpdateBulkL2(
                CreateLevel(pair, 103, 400, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair, 98, CryptoOrderSide.Bid),
                CreateLevel(pair, 102, CryptoOrderSide.Ask),
                CreateLevel(pair, 103, CryptoOrderSide.Ask)
            ));

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair, 100, CryptoOrderSide.Bid)
            ));

            source.StreamBulk(GetDeleteBulkL2(
                CreateLevel(pair, 99, CryptoOrderSide.Bid)
            ));

            Assert.Equal(0, updatedBidLevels[0].Index);
            Assert.Equal(1, updatedBidLevels[1].Index);
            Assert.Equal(2, updatedBidLevels[2].Index);
            Assert.Equal(0, updatedBidLevels[3].Index);
            Assert.Equal(1, updatedBidLevels[4].Index);
            Assert.Equal(0, updatedBidLevels[5].Index);
            Assert.Equal(2, updatedBidLevels[6].Index);
            Assert.Equal(1, updatedBidLevels[7].Index);
            Assert.Equal(2, updatedBidLevels[8].Index);
            Assert.Equal(0, updatedBidLevels[9].Index);
            Assert.Equal(0, updatedBidLevels[10].Index);

            Assert.Equal(0, updatedAskLevels[0].Index);
            Assert.Equal(1, updatedAskLevels[1].Index);
            Assert.Equal(2, updatedAskLevels[2].Index);
            Assert.Equal(0, updatedAskLevels[3].Index);
            Assert.Equal(2, updatedAskLevels[4].Index);
            Assert.Equal(1, updatedAskLevels[5].Index);
            Assert.Equal(2, updatedAskLevels[6].Index);
            Assert.Equal(1, updatedAskLevels[7].Index);
            Assert.Equal(2, updatedAskLevels[8].Index);
        }
    }
}
