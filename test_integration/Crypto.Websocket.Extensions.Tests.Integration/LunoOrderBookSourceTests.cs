using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.OrderBooks.SourcesL3;
using Luno.Client.Websocket.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Websocket.Client;
using Websocket.Client.Models;
using Xunit;
using Xunit.Abstractions;

namespace Crypto.Websocket.Extensions.Tests.Integration
{
    public class LunoOrderBookSourceTests
    {
        readonly ITestOutputHelper _testOutputHelper;
        readonly IList<long> _updateMessages = new List<long>();
        readonly IList<long> _topNUpdateMessages = new List<long>();

        public LunoOrderBookSourceTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        DateTime _timestamp;

        [Fact]
        public async Task StreamingRecordedMessages_ShouldHandleOrderBookCorrectly()
        {
            using var websocketClient = new FakeLunoWebsocketClient
            {
                FileNames = new[] { Path.Combine(Environment.CurrentDirectory, "Luno-XBTZAR-Websocket-Log.txt") },
                Delimiter = Environment.NewLine
            };
            const string pair = "XBTZAR";
            using var client = new LunoMarketWebsocketClient(NullLogger.Instance, websocketClient, pair);

            const int topLevelCount = 5;
            using var source = new LunoOrderBookL3Source(client)
            {
                //BufferEnabled = false
                BufferInterval = TimeSpan.FromMilliseconds(2)
            };
            using var orderBook = new CryptoOrderBook(pair, source)
            {
                NotifyForLevelAndAbove = topLevelCount
            };

            var orderBookIsConsistent = true;
            var orderBookUpdates = 0;
            var orderBookTopNUpdates = 0;

            using var heartbeatSubscription = orderBook.OrderBookUpdatedStream
                .Subscribe(info =>
                {
                    try
                    {
                        orderBookUpdates++;
                        var lastHeartbeatAt = DateTime.UtcNow;

                        if (_updateMessages.Count > 2 && _topNUpdateMessages.Last() != _updateMessages.Last())
                            _testOutputHelper.WriteLine($"Websocket updates are out of sync: {info.ServerSequence}.");

                        orderBookIsConsistent = info.Quotes.IsValid() && !CryptoMathUtils.IsSame(info.Quotes.Bid, info.Quotes.Ask);

                        _updateMessages.Add(info.ServerSequence!.Value);

                        if (lastHeartbeatAt.Subtract(_timestamp) > TimeSpan.FromMilliseconds(300))
                            _testOutputHelper.WriteLine(@$"Orderbook hasn't changed for more than 300 milliseconds: {info.ServerSequence}.
Ask: {info.Quotes.AskAmount,8} @ {info.Quotes.Ask,-8}
Bid: {info.Quotes.BidAmount,8} @ {info.Quotes.Bid,-8}");

                        else if (lastHeartbeatAt.Subtract(_timestamp) > TimeSpan.FromSeconds(3))
                            _testOutputHelper.WriteLine(@$"Orderbook hasn't changed for more than 3 seconds: {info.ServerSequence}.
Ask: {info.Quotes.AskAmount,8} @ {info.Quotes.Ask,-8}
Bid: {info.Quotes.BidAmount,8} @ {info.Quotes.Bid,-8}");

                    }
                    catch (Exception exception)
                    {
                        _testOutputHelper.WriteLine($"Error handling message: {info.ServerSequence}. {exception}");
                    }
                });

            using var orderBookTopNUpdatedSubscription = orderBook.TopNLevelsUpdatedStream
                .Subscribe(info =>
                {
                    try
                    {
                        _timestamp = DateTime.UtcNow;
                        orderBookTopNUpdates++;
                        _topNUpdateMessages.Add(info.ServerSequence!.Value);
                    }
                    catch (Exception exception)
                    {
                        _testOutputHelper.WriteLine($"Error handling message: {info.ServerSequence}. {exception}");
                    }
                });

            await websocketClient.Start();

            await Task.Delay(TimeSpan.FromSeconds(5));

            await websocketClient.Stopped;

            Assert.True(orderBookIsConsistent);

            Assert.InRange(orderBookUpdates, 8, 16);
            Assert.True(orderBookTopNUpdates < orderBookUpdates);
        }
    }

    static class Extensions
    {
        public static IDisposable SubscribeAsyncOmit<T>(this IObservable<T> source, Func<T, Task> onNext, int maximumConcurrencyCount = 1)
        {
            var semaphore = new SemaphoreSlim(maximumConcurrencyCount, maximumConcurrencyCount);

            var subscription = source
                .SelectMany(item => semaphore.Wait(0)
                    ? Observable.Return(Observable.FromAsync(async () => await onNext(item)).Finally(() => semaphore.Release()))
                    : Observable.Empty<IObservable<Unit>>())
                .Merge(maximumConcurrencyCount)
                .Subscribe();

            return new CompositeDisposable(semaphore, subscription);
        }
    }

    class FakeLunoWebsocketClient : IWebsocketClient
    {
        readonly Subject<ResponseMessage> _messageReceivedSubject = new();

        public IObservable<ResponseMessage> MessageReceived => _messageReceivedSubject.AsObservable();
        public IObservable<ReconnectionInfo> ReconnectionHappened => Observable.Empty<ReconnectionInfo>();
        public IObservable<DisconnectionInfo> DisconnectionHappened => Observable.Empty<DisconnectionInfo>();

        public TimeSpan? ReconnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan? ErrorReconnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public string Name { get; set; }
        public bool IsStarted { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsReconnectionEnabled { get; set; }
        public ClientWebSocket NativeClient { get; }
        public Encoding MessageEncoding { get; set; }

        public bool IsTextMessageConversionEnabled { get; set; }

        public string[] FileNames { get; set; }
        public string Delimiter { get; set; }
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        readonly TaskCompletionSource<bool> _stopped = new();
        public Task Stopped => _stopped.Task;

        readonly Random _random = new();

        public void StreamFakeMessage(ResponseMessage message)
        {
            throw new NotImplementedException();
        }

        public Uri Url { get; set; }

        public virtual void Dispose() { }

        public virtual Task Start()
        {
            StartStreaming();

            return Task.CompletedTask;
        }

        public Task StartOrFail()
        {
            return Task.CompletedTask;
        }

        public Task<bool> Stop(WebSocketCloseStatus status, string statusDescription)
        {
            return Task.FromResult(true);
        }

        public Task<bool> StopOrFail(WebSocketCloseStatus status, string statusDescription)
        {
            return Task.FromResult(true);
        }

        public virtual void Send(string message)
        {
        }

        public void Send(byte[] message)
        {
        }

        public void Send(ArraySegment<byte> message)
        {
        }

        public virtual Task SendInstant(string message)
        {
            return Task.CompletedTask;
        }

        public Task SendInstant(byte[] message)
        {
            return Task.CompletedTask;
        }

        public Task Reconnect()
        {
            return Task.CompletedTask;
        }

        public Task ReconnectOrFail()
        {
            throw new NotImplementedException();
        }

        private void StartStreaming()
        {
            if (FileNames == null)
                throw new InvalidOperationException("FileNames are not set, provide at least one path to historical data");
            if (string.IsNullOrEmpty(Delimiter))
                throw new InvalidOperationException("Delimiter is not set (separator between messages in the file)");

            foreach (var fileName in FileNames)
            {
                var fs = new FileStream(fileName, FileMode.Open);
                var stream = new StreamReader(fs, Encoding);
                using (stream)
                {
                    var message = ReadByDelimeter(stream, Delimiter);
                    while (message != null)
                    {
                        _messageReceivedSubject.OnNext(ResponseMessage.TextMessage(message));
                        Thread.Sleep(TimeSpan.FromMilliseconds((double)_random.Next(30, 100) / 100));
                        message = ReadByDelimeter(stream, Delimiter);
                    }
                }
            }

            _stopped.SetResult(true);
        }

        private static string ReadByDelimeter(StreamReader sr, string delimiter)
        {
            var line = new StringBuilder();
            int matchIndex = 0;

            while (sr.Peek() > 0)
            {
                var nextChar = (char)sr.Read();
                line.Append(nextChar);
                if (nextChar == delimiter[matchIndex])
                {
                    if (matchIndex == delimiter.Length - 1)
                    {
                        return line.ToString()[..(line.Length - delimiter.Length)];
                    }
                    matchIndex++;
                }
                else
                {
                    matchIndex = 0;
                }
            }

            return line.Length == 0 ? null : line.ToString();
        }
    }
}
