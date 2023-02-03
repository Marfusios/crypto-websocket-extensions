using System;
using System.Threading.Tasks;
using Bitfinex.Client.Websocket;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Requests.Subscriptions;
using Bitfinex.Client.Websocket.Utils;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Websocket.Client;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests.Integration
{
    public class BitfinexOrderBookSourceTests
    {
        [Fact]
        public async Task ConnectToSource_ShouldHandleOrderBookCorrectly()
        {
            var url = BitfinexValues.BitfinexPublicWebsocketUrl;
            using var communicator = new WebsocketClient(url);
            using var client = new BitfinexPublicWebsocketClient(NullLogger.Instance, communicator);
            const string pair = "BTCUSD";

            var source = new BitfinexOrderBookSource(client);
            var orderBook = new CryptoOrderBook(pair, source);
                    
            await communicator.Start();
            client.Send(new BookSubscribeRequest(pair, BitfinexPrecision.P0, BitfinexFrequency.Realtime, "100"));

            await Task.Delay(TimeSpan.FromSeconds(5));

            Assert.True(orderBook.BidPrice > 0);
            Assert.True(orderBook.AskPrice > 0);

            Assert.NotEmpty(orderBook.BidLevels);
            Assert.NotEmpty(orderBook.AskLevels);
        }

        [Fact]
        public async Task AutoSnapshotReloading_ShouldWorkAfterTimeout()
        {
            var url = BitfinexValues.BitfinexPublicWebsocketUrl;
            using var communicator = new WebsocketClient(url);
            using var client = new BitfinexPublicWebsocketClient(NullLogger.Instance, communicator);
            const string pair = "LTCUSD";

            var source = new BitfinexOrderBookSource(client)
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
