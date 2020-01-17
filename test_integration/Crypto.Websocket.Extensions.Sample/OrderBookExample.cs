using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Binance.Client.Websocket;
using Binance.Client.Websocket.Client;
using Binance.Client.Websocket.Subscriptions;
using Binance.Client.Websocket.Websockets;
using Bitfinex.Client.Websocket;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Requests.Configurations;
using Bitfinex.Client.Websocket.Utils;
using Bitfinex.Client.Websocket.Websockets;
using Bitmex.Client.Websocket;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Websockets;
using Coinbase.Client.Websocket;
using Coinbase.Client.Websocket.Channels;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Communicator;
using Coinbase.Client.Websocket.Requests;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Serilog;

namespace Crypto.Websocket.Extensions.Sample
{
    public static class OrderBookExample
    {
        public static async Task RunEverything()
        {
            var bitmexOb = await StartBitmex("XBTUSD", true);
            var bitfinexOb = await StartBitfinex("BTCUSD", true);
            var binanceOb = await StartBinance("BTCUSDT", true);
            var coinbaseOb = await StartCoinbase("BTC-USD", true);

            Log.Information("Waiting for price change...");

            Observable.CombineLatest(new[]
                {
                    bitmexOb.BidAskUpdatedStream,
                    bitfinexOb.BidAskUpdatedStream,
                    binanceOb.BidAskUpdatedStream,
                    coinbaseOb.BidAskUpdatedStream
                })
                .Subscribe(HandleQuoteChanged);
        }

        public static async Task RunOnlyOne()
        {
            var ob = await StartBitmex("XBTUSD", true);
            //var ob = await StartBinance("BTCUSDT", true);
            //var ob = await StartBitfinex("BTCUSD", true);
            //var ob = await StartCoinbase("BTC-USD", true);

            Log.Information("Waiting for price change...");

            Observable.CombineLatest(new[]
                {
                    ob.BidAskUpdatedStream
                })
                .Subscribe(HandleQuoteChanged);
        }

        private static void HandleQuoteChanged(IList<IOrderBookChangeInfo> quotes)
        {
            var formattedMessages = quotes
                .Select(x =>
                {
                    string time = string.Empty;
                    if (x.ServerTimestamp != null)
                    {
                        time = $" {x.ServerTimestamp:ss.ffffff}";
                    }

                    var metaString = $" (" +
                                   $"{x.Sources.Length} " +
                                   $"{x.Sources.Sum(y => y.Levels.Length)}" +
                                   $"{time})";
                    return
                            $"{x.ExchangeName.ToUpper()}{metaString} {x.Quotes.Bid.ToString("#.00#") + "/" + x.Quotes.Ask.ToString("#.00#"),16}";
                })
                .Select(x => $"{x,30}")
                .ToArray();

            var msg = string.Join(" | ", formattedMessages);
            Log.Information($"Quotes changed:  {msg}");
        }


        private static async Task<CryptoOrderBook> StartBitmex(string pair, bool optimized)
        {
            var url = BitmexValues.ApiWebsocketUrl;
            var communicator = new BitmexWebsocketCommunicator(url) { Name = "Bitmex" };
            var client = new BitmexWebsocketClient(communicator);

            var source = new BitmexOrderBookSource(client);
            var orderBook = new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            await communicator.Start();

            // Send subscription request to order book data
            client.Send(new Bitmex.Client.Websocket.Requests.BookSubscribeRequest(pair));

            return orderBook;
        }

        private static async Task<CryptoOrderBook> StartBitfinex(string pair, bool optimized)
        {
            var url = BitfinexValues.ApiWebsocketUrl;
            var communicator = new BitfinexWebsocketCommunicator(url) { Name = "Bitfinex" };
            var client = new BitfinexWebsocketClient(communicator);

            var source = new BitfinexOrderBookSource(client);
            var orderBook = new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            await communicator.Start();

            // Send configuration request to enable server timestamps
            client.Send(new ConfigurationRequest(ConfigurationFlag.Sequencing | ConfigurationFlag.Timestamp));

            // Send subscription request to order book data
            client.Send(new Bitfinex.Client.Websocket.Requests.Subscriptions.BookSubscribeRequest(pair, 
                BitfinexPrecision.P0, BitfinexFrequency.Realtime, "100"));

            return orderBook;
        }

        private static async Task<CryptoOrderBook> StartBinance(string pair, bool optimized)
        {
            var url = BinanceValues.ApiWebsocketUrl;
            var communicator = new BinanceWebsocketCommunicator(url) { Name = "Binance" };
            var client = new BinanceWebsocketClient(communicator);

            client.SetSubscriptions(
                new OrderBookDiffSubscription(pair)
            );

            var source = new BinanceOrderBookSource(client);
            var orderBook = new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            await communicator.Start();

            // Binance is special
            // We need to load snapshot in advance manually via REST call
            await source.LoadSnapshot(communicator, pair);

            return orderBook;
        }

        private static async Task<CryptoOrderBook> StartCoinbase(string pair, bool optimized)
        {
            var url = CoinbaseValues.ApiWebsocketUrl;
            var communicator = new CoinbaseWebsocketCommunicator(url) { Name = "Coinbase" };
            var client = new CoinbaseWebsocketClient(communicator);

            var source = new CoinbaseOrderBookSource(client);
            var orderBook = new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            await communicator.Start();

            // Send subscription request to order book data
            client.Send(new SubscribeRequest(
                new[] { pair },
                ChannelSubscriptionType.Level2
            ));

            return orderBook;
        }

        private static void ConfigureOptimized(IOrderBookLevel2Source source, CryptoOrderBook orderBook)
        {
            source.BufferEnabled = true;
            source.BufferInterval = TimeSpan.FromMilliseconds(0);

            orderBook.DebugEnabled = false;
            orderBook.DebugLogEnabled = false;
            orderBook.ValidityCheckEnabled = false;
        }
    }
}
