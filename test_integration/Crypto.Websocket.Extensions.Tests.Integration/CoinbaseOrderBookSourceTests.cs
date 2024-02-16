using System;
using System.Threading.Tasks;
using Coinbase.Client.Websocket;
using Coinbase.Client.Websocket.Channels;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Communicator;
using Coinbase.Client.Websocket.Requests;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests.Integration
{
    public class CoinbaseOrderBookSourceTests
    {
        [Fact(Skip = "Coinbase no longer working")]
        public async Task ConnectToSource_ShouldHandleOrderBookCorrectly()
        {
            var url = CoinbaseValues.ApiWebsocketUrl;
            using (var communicator = new CoinbaseWebsocketCommunicator(url))
            {
                using (var client = new CoinbaseWebsocketClient(communicator))
                {
                    var pair = "BTC-USD";

                    var source = new CoinbaseOrderBookSource(client);
                    var orderBook = new CryptoOrderBook(pair, source);

                    await communicator.Start();

                    client.Send(new SubscribeRequest(
                        new[] { pair },
                        ChannelSubscriptionType.Level2
                        ));

                    await Task.Delay(TimeSpan.FromSeconds(5));

                    Assert.True(orderBook.BidPrice > 0);
                    Assert.True(orderBook.AskPrice > 0);

                    Assert.NotEmpty(orderBook.BidLevels);
                    Assert.NotEmpty(orderBook.AskLevels);
                }
            }
        }

        [Fact]
        public async Task AutoSnapshotReloading_ShouldWorkAfterTimeout()
        {
            var url = CoinbaseValues.ApiWebsocketUrl;
            using (var communicator = new CoinbaseWebsocketCommunicator(url))
            {
                using (var client = new CoinbaseWebsocketClient(communicator))
                {
                    var pair = "BTC-USD";

                    var source = new CoinbaseOrderBookSource(client)
                    {
                        LoadSnapshotEnabled = true
                    };
                    var orderBook = new CryptoOrderBook(pair, source)
                    {
                        SnapshotReloadTimeout = TimeSpan.FromSeconds(2),
                        SnapshotReloadEnabled = true
                    };

                    await Task.Delay(TimeSpan.FromSeconds(20));

                    Assert.True(orderBook.BidPrice > 0);
                    Assert.True(orderBook.AskPrice > 0);

                    Assert.NotEmpty(orderBook.BidLevels);
                    Assert.NotEmpty(orderBook.AskLevels);
                }
            }
        }
    }
}
