using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Bitfinex.Client.Websocket;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Requests.Configurations;
using Bitfinex.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.OrderBooks.SourcesL3;
using Serilog;

namespace Crypto.Websocket.Extensions.Sample
{
    public static class OrderBookL3Example
    {
        public static async Task RunOnlyOne()
        {
            var optimized = true;

            var ob = await StartBitfinex("BTCUSD", optimized);

            Log.Information("Waiting for price change...");

            Observable.CombineLatest(new[]
                {
                    ob.OrderBookUpdatedStream
                })
                .Subscribe(x => HandleQuoteChanged(x, false, ob));
        }

        private static void HandleQuoteChanged(IList<IOrderBookChangeInfo> quotes, bool simple, CryptoOrderBook ob)
        {
            var bids = ob.BidLevelsPerPrice.Take(2).SelectMany(x => x.Value).ToArray();
            var asks = ob.AskLevelsPerPrice.Take(2).SelectMany(x => x.Value).ToArray();

            var max = Math.Max(bids.Length, asks.Length);

            var msg = string.Empty;

            for (int i = 0; i < max; i++)
            {
                var bid = bids.Length > i ? bids[i] : null;
                var ask = asks.Length > i ? asks[i] : null;

                var bidMsg =
                    bid != null ? $"#{i+1} {bid?.Id} {"p: " + (bid?.Price ?? 0).ToString("#.00#") + " a: " + (bid?.Amount ?? 0).ToString("0.00#")}" : " ";
                var askMsg =
                    ask != null ? $"#{i+1} {ask?.Id} {"p: " + (ask?.Price ?? 0).ToString("#.00#") + " a: " + (ask?.Amount ?? 0).ToString("0.00#")}" : " ";

                bidMsg = $"{bidMsg,50}";
                askMsg = $"{askMsg,50}";

                msg+= $"{Environment.NewLine}{bidMsg}  {askMsg}";
                
            }

            Log.Information($"TOP LEVEL: {msg}{Environment.NewLine}");

        }


       

        private static async Task<CryptoOrderBook> StartBitfinex(string pair, bool optimized)
        {
            var url = BitfinexValues.ApiWebsocketUrl;
            var communicator = new BitfinexWebsocketCommunicator(url) { Name = "Bitfinex" };
            var client = new BitfinexWebsocketClient(communicator);

            var source = new BitfinexOrderBookL3Source(client);
            var orderBook = new CryptoOrderBook(pair, source, CryptoOrderBookType.L3);

            if (optimized)
            {
                ConfigureOptimized(source, orderBook);
            }

            _ = communicator.Start();

            // Send configuration request to enable server timestamps
            client.Send(new ConfigurationRequest(ConfigurationFlag.Sequencing | ConfigurationFlag.Timestamp));

            // Send subscription request to raw order book data
            client.Send(new Bitfinex.Client.Websocket.Requests.Subscriptions.RawBookSubscribeRequest(pair,"100"));

            return orderBook;
        }

        private static void ConfigureOptimized(IOrderBookSource source, ICryptoOrderBook orderBook)
        {
            source.BufferEnabled = true;
            source.BufferInterval = TimeSpan.FromMilliseconds(0);

            orderBook.DebugEnabled = false;
            orderBook.DebugLogEnabled = false;
            orderBook.ValidityCheckEnabled = false;
        }
    }
}
