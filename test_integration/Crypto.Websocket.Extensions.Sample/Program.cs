using System;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Threading;
using Serilog;
using Serilog.Events;

namespace Crypto.Websocket.Extensions.Sample
{
    class Program
    {
        static readonly ManualResetEvent ExitEvent = new(false);

        static void Main(string[] _)
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            InitLogging();

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



            OrderBookExample.RunEverything();
            //OrderBookExample.RunOnlyOne(false).Wait();
            //OrderBookL3Example.RunOnlyOne().Wait();

            //TradesExample.RunEverything().Wait();

            //OrdersExample.RunEverything().Wait();


            ExitEvent.WaitOne();

            Log.Debug("====================================");
            Log.Debug("              STOPPING              ");
            Log.Debug("====================================");
            Log.CloseAndFlush();
        }


        static void InitLogging()
        {
            var executingDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location);
            var logPath = Path.Combine(executingDir, "logs", "verbose.log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                .WriteTo.ColoredConsole(LogEventLevel.Debug, 
                    outputTemplate: "{Timestamp:HH:mm:ss.ffffff} [{Level:u3}] {Message}{NewLine}")
                .CreateLogger();
        }

        static void CurrentDomainOnProcessExit(object sender, EventArgs eventArgs)
        {
            Log.Warning("Exiting process");
            ExitEvent.Set();
        }

        static void DefaultOnUnloading(AssemblyLoadContext assemblyLoadContext)
        {
            Log.Warning("Unloading process");
            ExitEvent.Set();
        }

        static void ConsoleOnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Log.Warning("Canceling process");
            e.Cancel = true;
            ExitEvent.Set();
        }
    }
}
