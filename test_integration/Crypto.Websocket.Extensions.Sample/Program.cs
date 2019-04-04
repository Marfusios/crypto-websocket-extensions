using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Bitmex.Client.Websocket;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.OrderBooks;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Serilog;
using Serilog.Events;

namespace Crypto.Websocket.Extensions.Sample
{
    class Program
    {
        private static readonly ManualResetEvent ExitEvent = new ManualResetEvent(false);

        static void Main(string[] args)
        {
            InitLogging();

            AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
            AssemblyLoadContext.Default.Unloading += DefaultOnUnloading;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            Console.WriteLine("|=======================|");
            Console.WriteLine("|     BINANCE CLIENT    |");
            Console.WriteLine("|=======================|");
            Console.WriteLine();

            Log.Debug("====================================");
            Log.Debug("              STARTING              ");
            Log.Debug("====================================");
           


            //var url = BinanceValues.ApiWebsocketUrl;
            //using (var communicator = new BinanceWebsocketCommunicator(url))
            //{
            //    communicator.Name = "Binance-1";
            //    communicator.ReconnectTimeoutMs = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
            //    communicator.ReconnectionHappened.Subscribe(type =>
            //        Log.Information($"Reconnection happened, type: {type}"));

            //    using (var client = new BinanceWebsocketClient(communicator))
            //    {
            //        SubscribeToStreams(client);

            //        client.SetSubscriptions(
            //            new TradeSubscription("btcusdt"),
            //            new TradeSubscription("ethbtc"),
            //            new TradeSubscription("bnbusdt"),
            //            new AggregateTradeSubscription("bnbusdt"),
            //            new OrderBookPartialSubscription("btcusdt", 5),
            //            new OrderBookPartialSubscription("bnbusdt", 10),
            //            new OrderBookDiffSubscription("ltcusdt")
            //            );
            //        communicator.Start().Wait();

            //        ExitEvent.WaitOne();
            //    }
            //}

            Log.Debug("====================================");
            Log.Debug("              STOPPING              ");
            Log.Debug("====================================");
            Log.CloseAndFlush();
        }

        private static async Task StartBitmex()
        {
            var url = BitmexValues.ApiWebsocketUrl;
            var communicator = new BitmexWebsocketCommunicator(url);
            var client = new BitmexWebsocketClient(communicator);

            var pair = "XBTUSD";

            var source = new BitmexOrderBookSource(client);
            var orderBook = new CryptoOrderBook(pair, source);

            // orderBook.BidAskUpdatedStream.Subscribe(xxx)
            orderBook.OrderBookUpdatedStream.Subscribe(quotes =>
            {
                var currentBid = orderBook.BidPrice;
                var currentAsk = orderBook.AskPrice;

                var bids = orderBook.BidLevels;
                // xxx
            });
                    
            await communicator.Start();
        }

        private static void InitLogging()
        {
            var executingDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var logPath = Path.Combine(executingDir, "logs", "verbose.log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .WriteTo.ColoredConsole(LogEventLevel.Debug)
                .CreateLogger();
        }

        private static void CurrentDomainOnProcessExit(object sender, EventArgs eventArgs)
        {
            Log.Warning("Exiting process");
            ExitEvent.Set();
        }

        private static void DefaultOnUnloading(AssemblyLoadContext assemblyLoadContext)
        {
            Log.Warning("Unloading process");
            ExitEvent.Set();
        }

        private static void ConsoleOnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Log.Warning("Canceling process");
            e.Cancel = true;
            ExitEvent.Set();
        }
    }
}
