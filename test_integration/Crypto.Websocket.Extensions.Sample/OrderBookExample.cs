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
using Bitstamp.Client.Websocket;
using Bitstamp.Client.Websocket.Client;
using Bitstamp.Client.Websocket.Communicator;
using Coinbase.Client.Websocket;
using Coinbase.Client.Websocket.Channels;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Communicator;
using Coinbase.Client.Websocket.Requests;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Microsoft.Extensions.Logging;
using Serilog;
using Channel = Bitstamp.Client.Websocket.Channels.Channel;

namespace Crypto.Websocket.Extensions.Sample
{
    public static class OrderBookExample
    {
        public static async Task RunEverything()
        {
            var optimized = false;
            var l2OrderBook = false;

            var bitmexOb = await StartBitmex("XBTUSD", optimized, l2OrderBook);
            var bitfinexOb = await StartBitfinex("BTCUSD", optimized, l2OrderBook);
            var binanceOb = await StartBinance("BTCUSDT", optimized, l2OrderBook);
            //var coinbaseOb = await StartCoinbase("BTC-USD", optimized, l2OrderBook);
            var bitstampOb = await StartBitstamp("BTCUSD", optimized, l2OrderBook);
            //var huobiOb = await StartHuobi("btcusdt", optimized, l2OrderBook);

            Log.Information("Waiting for price change...");

            Observable.CombineLatest(new[]
                {
                    bitmexOb.BidAskUpdatedStream,
                    bitfinexOb.BidAskUpdatedStream,
                    binanceOb.BidAskUpdatedStream,
                    //coinbaseOb.BidAskUpdatedStream,
                    bitstampOb.BidAskUpdatedStream
                    //huobiOb.BidAskUpdatedStream
                })
                .Subscribe(x => HandleQuoteChanged(x, true));
        }

        public static async Task RunOnlyOne(bool displayFullOb)
        {
            var optimized = true;
            var l2OrderBook = false;

            //var ob = await StartBitmex("XBTUSD", optimized, l2OrderBook);
            var ob = await StartBinance("BTCUSDT", optimized, l2OrderBook);
            //var ob = await StartBitfinex("BTCUSD", optimized, l2OrderBook);
            //var ob = await StartCoinbase("BTC-USD", optimized, l2OrderBook);
            //var ob = await StartBitstamp("BTCUSD", optimized, l2OrderBook);
            //var ob = await StartHuobi("btcusdt", optimized, l2OrderBook);}}

            Log.Information("Waiting for price change...");

            if (displayFullOb)
            {
                ob.OrderBookUpdatedStream
                    .Subscribe(x => HandleQuoteChanged(ob));
            }
            else
            {
                Observable.CombineLatest(new[]
                    {
                        ob.BidAskUpdatedStream
                    })
                    .Subscribe(x => HandleQuoteChanged(x, false));
            }

        }

        private static void HandleQuoteChanged(IList<IOrderBookChangeInfo> quotes, bool simple)
        {
            var formattedMessages = quotes
                .Select(x =>
                {
                    string time = string.Empty;
                    if (x.ServerTimestamp != null)
                    {
                        time = $" {x.ServerTimestamp:ss.ffffff}";
                    }

                    var metaString = simple ? string.Empty : $" (" +
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

        private static void HandleQuoteChanged(ICryptoOrderBook ob)
        {
            var bids = ob.BidLevels.Take(10).ToArray();
            var asks = ob.AskLevels.Take(10).ToArray();

            var max = Math.Max(bids.Length, asks.Length);

            var msg = string.Empty;

            for (int i = 0; i < max; i++)
            {
                var bid = bids.Length > i ? bids[i] : null;
                var ask = asks.Length > i ? asks[i] : null;

                var bidMsg =
                    bid != null ? $"#{i + 1} " +
                                  $"{"p: " + (bid?.Price ?? 0).ToString("#.00##") + " a: " + (bid?.Amount ?? 0).ToString("0.00#")} " +
                                  $"[{bid.AmountUpdatedCount}]"
                        : " ";
                var askMsg =
                    ask != null ? $"#{i + 1} " +
                                  $"{"p: " + (ask?.Price ?? 0).ToString("#.00##") + " a: " + (ask?.Amount ?? 0).ToString("0.00#")} " +
                                  $"[{ask.AmountUpdatedCount}]"
                        : " ";

                bidMsg = $"{bidMsg,50}";
                askMsg = $"{askMsg,50}";

                msg += $"{Environment.NewLine}{bidMsg}  {askMsg}";

            }

            Log.Information($"ORDER BOOK {ob.ExchangeName} {ob.TargetPairOriginal}: {msg}{Environment.NewLine}");

        }


        private static async Task<ICryptoOrderBook> StartBitmex(string pair, bool optimized, bool l2Optimized)
        {
            var url = BitmexValues.ApiWebsocketUrl;
            var communicator = new BitmexWebsocketCommunicator(url, Program.Logger.CreateLogger<BitmexWebsocketCommunicator>()) { Name = "Bitmex" };
            var client = new BitmexWebsocketClient(communicator, Program.Logger.CreateLogger<BitmexWebsocketClient>());

            var source = new BitmexOrderBookSource(client);
            var orderBook = l2Optimized ?
                new CryptoOrderBookL2(pair, source) :
                (ICryptoOrderBook)new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            _ = communicator.Start();

            // Send subscription request to order book data
            client.Send(new Bitmex.Client.Websocket.Requests.BookSubscribeRequest(pair));

            return orderBook;
        }

        private static async Task<ICryptoOrderBook> StartBitfinex(string pair, bool optimized, bool l2Optimized)
        {
            var url = BitfinexValues.ApiWebsocketUrl;
            var communicator = new BitfinexWebsocketCommunicator(url, Program.Logger.CreateLogger<BitfinexWebsocketCommunicator>()) { Name = "Bitfinex" };
            var client = new BitfinexWebsocketClient(communicator, Program.Logger.CreateLogger<BitfinexWebsocketClient>());

            var source = new BitfinexOrderBookSource(client);
            var orderBook = l2Optimized ?
                new CryptoOrderBookL2(pair, source) :
                (ICryptoOrderBook)new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            _ = communicator.Start();

            // Send configuration request to enable server timestamps
            client.Send(new ConfigurationRequest(ConfigurationFlag.Sequencing | ConfigurationFlag.Timestamp));

            // Send subscription request to order book data
            client.Send(new Bitfinex.Client.Websocket.Requests.Subscriptions.BookSubscribeRequest(pair,
                BitfinexPrecision.P0, BitfinexFrequency.Realtime, "100"));

            return orderBook;
        }

        private static async Task<ICryptoOrderBook> StartBinance(string pair, bool optimized, bool l2Optimized)
        {
            var url = BinanceValues.ApiWebsocketUrl;
            var communicator = new BinanceWebsocketCommunicator(url, Program.Logger.CreateLogger<BinanceWebsocketCommunicator>()) { Name = "Binance" };
            var client = new BinanceWebsocketClient(communicator, Program.Logger.CreateLogger<BinanceWebsocketClient>());

            client.SetSubscriptions(
                new OrderBookDiffSubscription(pair)
            );

            var source = new BinanceOrderBookSource(client);
            var orderBook = l2Optimized ?
                new CryptoOrderBookL2(pair, source) :
                (ICryptoOrderBook)new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            _ = communicator.Start();

            // Binance is special
            // We need to load snapshot in advance manually via REST call
            _ = source.LoadSnapshot(communicator, pair);

            return orderBook;
        }

        private static async Task<ICryptoOrderBook> StartCoinbase(string pair, bool optimized, bool l2Optimized)
        {
            var url = CoinbaseValues.ApiWebsocketUrl;
            var communicator = new CoinbaseWebsocketCommunicator(url, Program.Logger.CreateLogger<CoinbaseWebsocketCommunicator>()) { Name = "Coinbase" };
            var client = new CoinbaseWebsocketClient(communicator, Program.Logger.CreateLogger<CoinbaseWebsocketClient>());

            var source = new CoinbaseOrderBookSource(client);
            var orderBook = l2Optimized ?
                new CryptoOrderBookL2(pair, source) :
                (ICryptoOrderBook)new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            _ = communicator.Start();

            // Send subscription request to order book data
            client.Send(new SubscribeRequest(
                new[] { pair },
                ChannelSubscriptionType.Level2
            ));

            return orderBook;
        }

        private static async Task<ICryptoOrderBook> StartBitstamp(string pair, bool optimized, bool l2Optimized)
        {
            var url = BitstampValues.ApiWebsocketUrl;
            var communicator = new BitstampWebsocketCommunicator(url, Program.Logger.CreateLogger<BitstampWebsocketCommunicator>()) { Name = "Bitstamp" };
            var client = new BitstampWebsocketClient(communicator, Program.Logger.CreateLogger<BitstampWebsocketClient>());

            var source = new BitstampOrderBookSource(client);
            var orderBook = l2Optimized ?
                new CryptoOrderBookL2(pair, source) :
                (ICryptoOrderBook)new CryptoOrderBook(pair, source);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            _ = communicator.Start();

            // Send subscription request to order book data
            client.Send(new Bitstamp.Client.Websocket.Requests.SubscribeRequest(
                pair,
                Channel.OrderBook
            ));

            return orderBook;
        }

        //private static async Task<ICryptoOrderBook> StartHuobi(string pair, bool optimized, bool l2Optimized)
        //{
        //    var config = new HuobiMarketByPriceWebsocketClientConfig
        //    {
        //        Url = HuobiConstants.ApiMbpWebsocketUrl,
        //        CommunicatorName = "Huobi"
        //    };
        //    var loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);

        //    var client = HuobiWebsocketClientsFactory.CreateMarketByPriceClient(config, loggerFactory);
        //    var source = new HuobiOrderBookSource(client);

        //    var orderBook = l2Optimized ?
        //        new CryptoOrderBookL2(pair, source) :
        //        (ICryptoOrderBook)new CryptoOrderBook(pair, source);

        //    if (optimized)
        //    {
        //        ConfigureOptimized(source, orderBook);
        //    }

        //    _ = client.Start();

        //    // send subscription request to order book data
        //    client.Send(new MarketByPriceSubscribeRequest("s1", pair, 20));

        //    // send request to snapshot
        //    client.Send(new MarketByPricePullRequest("p1", pair, 20));

        //    return orderBook;
        //}

        private static void ConfigureOptimized(IOrderBookSource source, ICryptoOrderBook orderBook)
        {
            source.BufferEnabled = true;
            source.BufferInterval = TimeSpan.FromMilliseconds(0);

            orderBook.DebugEnabled = true;
            orderBook.DebugLogEnabled = true;
            orderBook.ValidityCheckEnabled = false;
        }
    }
}
