﻿using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Binance.Client.Websocket;
using Binance.Client.Websocket.Client;
using Binance.Client.Websocket.Subscriptions;
using Binance.Client.Websocket.Websockets;
using Bitfinex.Client.Websocket;
using Bitfinex.Client.Websocket.Client;
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
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Trades.Sources;
using Huobi.Client.Websocket;
using Huobi.Client.Websocket.Clients;
using Huobi.Client.Websocket.Config;
using Huobi.Client.Websocket.Messages.MarketData.MarketTradeDetail;
using Microsoft.Extensions.Logging;
using Serilog;
using Websocket.Client;
using Channel = Bitstamp.Client.Websocket.Channels.Channel;

namespace Crypto.Websocket.Extensions.Sample
{
    public static class TradesExample
    {
        public static async Task RunEverything()
        {
            var bitmex = GetBitmex("XBTUSD", false);
            var bitfinex = GetBitfinex("BTCUSD");
            var binance = GetBinance("BTCUSDT");
            var coinbase = GetCoinbase("BTC-USD");
            var bitstamp = GetBitstamp("BTCUSD");
            var huobi = GetHuobi("btcusdt");

            LogTrades(bitmex.Item1);
            LogTrades(bitfinex.Item1);
            LogTrades(binance.Item1);
            LogTrades(coinbase.Item1);
            LogTrades(bitstamp.Item1);
            LogTrades(huobi.Item1);

            Log.Information("Waiting for trades...");

            //_ = bitmex.Item2.Start();
            //_ = bitfinex.Item2.Start();
            //_ = binance.Item2.Start();
            //_ = coinbase.Item2.Start();
            _ = bitstamp.Item2.Start();
            //_ = huobi.Item2.Start();
        }

        private static void LogTrades(ITradeSource source)
        {
            source.TradesStream
                .SelectMany(y => y, (trades, trade) => new { trade = trade, count = trades.Length })
                .Subscribe(info =>
                {
                    var x = info.trade;
                    var group = info.count > 1 ? "group" : "single";
                    var side = $"[{x.Side}]";
                    var price = $"{x.Price:0.00}";
                    var amount = $"{x.Amount:0.00000000}/{x.AmountQuote:0}";
                    var time = $"{x.Timestamp:ss.ffffff} {x.ServerTimestamp:ss.ffffff}";
                    Log.Information($"{time,20} | {source.ExchangeName,10} {x.PairClean,7} {side,6} " +
                                    $" price: {price,10},  amount: {amount,20} " +
                                    $"({group}) " +
                                    $"maker: {x.MakerOrderId} " +
                                    $"taker: {x.TakerOrderId}");
                });
        }

        private static (ITradeSource, IWebsocketClient) GetBitmex(string pair, bool isTestnet)
        {
            var url = isTestnet ? BitmexValues.ApiWebsocketTestnetUrl : BitmexValues.ApiWebsocketUrl;
            var communicator = new BitmexWebsocketCommunicator(url) { Name = "Bitmex" };
            var client = new BitmexWebsocketClient(communicator);

            var source = new BitmexTradeSource(client);

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                client.Send(new Bitmex.Client.Websocket.Requests.TradesSubscribeRequest(pair));
            });

            return (source, communicator);
        }

        private static (ITradeSource, IWebsocketClient) GetBitfinex(string pair)
        {
            var url = BitfinexValues.ApiWebsocketUrl;
            var communicator = new BitfinexWebsocketCommunicator(url) { Name = "Bitfinex" };
            var client = new BitfinexWebsocketClient(communicator);

            var source = new BitfinexTradeSource(client);

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                client.Send(new Bitfinex.Client.Websocket.Requests.Subscriptions.TradesSubscribeRequest(pair));
            });

            return (source, communicator);
        }

        private static (ITradeSource, IWebsocketClient) GetBinance(string pair)
        {
            var url = BinanceValues.ApiWebsocketUrl;
            var communicator = new BinanceWebsocketCommunicator(url) { Name = "Binance" };
            var client = new BinanceWebsocketClient(communicator);

            var source = new BinanceTradeSource(client);

            client.SetSubscriptions(
                new TradeSubscription(pair)
            );

            return (source, communicator);
        }

        private static (ITradeSource, IWebsocketClient) GetCoinbase(string pair)
        {
            var url = CoinbaseValues.ApiWebsocketUrl;
            var communicator = new CoinbaseWebsocketCommunicator(url) { Name = "Coinbase" };
            var client = new CoinbaseWebsocketClient(communicator);

            var source = new CoinbaseTradeSource(client);

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                client.Send(new SubscribeRequest(
                    new[] { pair },
                    ChannelSubscriptionType.Matches
                ));
            });

            return (source, communicator);
        }

        private static (ITradeSource, IWebsocketClient) GetBitstamp(string pair)
        {
            var url = BitstampValues.ApiWebsocketUrl;
            var communicator = new BitstampWebsocketCommunicator(url) { Name = "Bitstamp" };
            var client = new BitstampWebsocketClient(communicator);

            var source = new BitstampTradeSource(client);

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                client.Send(new Bitstamp.Client.Websocket.Requests.SubscribeRequest(
                    pair,
                    Channel.Ticker
                ));
            });

            return (source, communicator);
        }

        private static (ITradeSource, IWebsocketClient) GetHuobi(string pair)
        {
            var config = new HuobiMarketWebsocketClientConfig
            {
                Url = HuobiConstants.ApiWebsocketUrl,
                CommunicatorName = "Huobi"
            };
            var loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);

            var client = HuobiWebsocketClientsFactory.CreateMarketClient(config, loggerFactory);
            var source = new HuobiTradeSource(client);

            client.Communicator.ReconnectionHappened.Subscribe(
                x =>
                {
                    var subscribeRequest = new MarketTradeDetailSubscribeRequest("id1", pair);
                    client.Send(subscribeRequest);
                });

            return (source, client.Communicator);
        }
    }
}
