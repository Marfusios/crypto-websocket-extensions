﻿using System;
using System.Threading.Tasks;
using Bitmex.Client.Websocket;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Requests;
using Bitmex.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.OrderBooks;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests.Integration
{
    public class BitmexOrderBookSourceTests
    {
        [Fact]
        public async Task ConnectToSource_ShouldHandleOrderBookCorrectly()
        {
            var url = BitmexValues.ApiWebsocketUrl;
            using (var communicator = new BitmexWebsocketCommunicator(url))
            {
                using (var client = new BitmexWebsocketClient(communicator))
                {
                    var pair = "XBTUSD";

                    var source = new BitmexOrderBookSource(client);
                    var orderBook = new CryptoOrderBook(pair, source);
                    
                    await communicator.Start();
                    await client.Send(new BookSubscribeRequest(pair));

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
            var url = BitmexValues.ApiWebsocketUrl;
            using (var communicator = new BitmexWebsocketCommunicator(url))
            {
                using (var client = new BitmexWebsocketClient(communicator))
                {
                    var pair = "XBTUSD";

                    var source = new BitmexOrderBookSource(client)
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
    }
}
