using System;
using System.Threading.Tasks;
using Binance.Client.Websocket;
using Binance.Client.Websocket.Client;
using Binance.Client.Websocket.Subscriptions;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Websocket.Client;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests.Integration
{
    public class BinanceOrderBookSourceTests
    {
        [Fact]
        public async Task ConnectToSource_ShouldHandleOrderBookCorrectly()
        {
            var url = BinanceValues.ApiWebsocketUrl;
            using var communicator = new WebsocketClient(url);
            const string pair = "BTCUSDT";
            using var client = new BinanceWebsocketClient(NullLogger.Instance, communicator, new OrderBookDiffSubscription(pair));

            var source = new BinanceOrderBookSource(client);
            var orderBook = new CryptoOrderBook(pair, source);

            await communicator.Start();

            // Binance is special
            // We need to load snapshot in advance manually via REST call
            await source.LoadSnapshot(pair);

            await Task.Delay(TimeSpan.FromSeconds(5));

            Assert.True(orderBook.BidPrice > 0);
            Assert.True(orderBook.AskPrice > 0);

            Assert.NotEmpty(orderBook.BidLevels);
            Assert.NotEmpty(orderBook.AskLevels);
        }

        [Fact]
        public async Task AutoSnapshotReloading_ShouldWorkAfterTimeout()
        {
            var url = BinanceValues.ApiWebsocketUrl;
            using var communicator = new WebsocketClient(url);
            const string pair = "BTCUSDT";
            using var client = new BinanceWebsocketClient(NullLogger.Instance, communicator, new OrderBookDiffSubscription(pair));

            var source = new BinanceOrderBookSource(client)
            {
                LoadSnapshotEnabled = true
            };
            var orderBook = new CryptoOrderBook(pair, source)
            {
                SnapshotReloadTimeout = TimeSpan.FromSeconds(5),
                SnapshotReloadEnabled = true
            };

            await Task.Delay(TimeSpan.FromSeconds(13));

            Assert.True(orderBook.BidPrice > 0);
            Assert.True(orderBook.AskPrice > 0);

            Assert.NotEmpty(orderBook.BidLevels);
            Assert.NotEmpty(orderBook.AskLevels);
        }
    }
}
