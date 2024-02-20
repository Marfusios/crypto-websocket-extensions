using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Threading;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Sample
{
    class Program
    {
        public static SerilogLoggerFactory Logger;
        private static readonly ManualResetEvent ExitEvent = new ManualResetEvent(false);

        static void Main(string[] args)
        {
            var defaultCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;
            
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            Logger = InitLogging();

            AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
            AssemblyLoadContext.Default.Unloading += DefaultOnUnloading;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            Console.WriteLine("|========================|");
            Console.WriteLine("|  WEBSOCKET EXTENSIONS  |");
            Console.WriteLine("|========================|");
            Console.WriteLine();

            Log.Debug("====================================");
            Log.Debug("              STARTING              ");
            Log.Debug("====================================");


            OrderBookExample.RunEverything().Wait();
            //OrderBookExample.RunOnlyOne(true).Wait();
            //OrderBookL3Example.RunOnlyOne().Wait();

            //TradesExample.RunEverything().Wait();

            //OrdersExample.RunEverything().Wait();


            ExitEvent.WaitOne();

            Log.Debug("====================================");
            Log.Debug("              STOPPING              ");
            Log.Debug("====================================");
            Log.CloseAndFlush();
        }




        private static SerilogLoggerFactory InitLogging()
        {
            var executingDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
            var logPath = Path.Combine(executingDir, "logs", "verbose.log");
            var logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .WriteTo.Console(LogEventLevel.Debug,
                    outputTemplate: "{Timestamp:HH:mm:ss.ffffff} [{Level:u3}] {Message}{NewLine}")
                .CreateLogger();
            Log.Logger = logger;
            return new SerilogLoggerFactory(logger);
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
