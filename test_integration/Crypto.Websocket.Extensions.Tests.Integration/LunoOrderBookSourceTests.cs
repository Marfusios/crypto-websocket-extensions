using System;
using System.IO;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.OrderBooks.SourcesL3;
using Luno.Client.Websocket.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Websocket.Client;
using Xunit;
using Xunit.Abstractions;

namespace Crypto.Websocket.Extensions.Tests.Integration
{
    public class LunoOrderBookSourceTests(ITestOutputHelper testOutputHelper)
    {
        [Fact]
        public async Task StreamingRecordedMessages_ShouldHandleOrderBookCorrectly()
        {
            using var websocketClient = new FakeLunoWebsocketClient();
            websocketClient.FileNames = [Path.Combine(Environment.CurrentDirectory, "Luno-XBTZAR-Websocket-Log.txt")];
            websocketClient.Delimiter = Environment.NewLine;
            const string pair = "XBTZAR";
            using var client = new LunoMarketWebsocketClient(NullLogger.Instance, websocketClient, pair);

            using var source = new LunoOrderBookL3Source(client, NullLogger.Instance);
            using var orderBook = new CryptoOrderBook(pair, source);

            var orderBookIsConsistent = true;

            using var heartbeatSubscription = orderBook.OrderBookUpdatedStream
                .Subscribe(info => orderBookIsConsistent &= info.Quotes.IsValid() && !CryptoMathUtils.IsSame(info.Quotes.Bid, info.Quotes.Ask));

            await websocketClient.Start();

            await Task.Delay(TimeSpan.FromSeconds(5));

            await websocketClient.Stopped;

            Assert.True(orderBook.BidPrice > 0);
            Assert.True(orderBook.AskPrice > 0);

            Assert.NotEmpty(orderBook.BidLevels);
            Assert.NotEmpty(orderBook.AskLevels);

			Assert.True(orderBookIsConsistent);
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
        public TimeSpan? LostReconnectTimeout { get; set; }
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

        public virtual bool Send(string message)
        {
            return true;
        }

        public bool Send(byte[] message)
        {
            return true;
		}

        public bool Send(ArraySegment<byte> message)
        {
            return true;
		}

        public virtual Task SendInstant(string message)
        {
            return Task.CompletedTask;
        }

        public Task SendInstant(byte[] message)
        {
            return Task.CompletedTask;
        }

        public bool SendAsText(byte[] message)
        {
			return true;
		}
        public bool SendAsText(ArraySegment<byte> message)
        {
			return true;
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
                        //Thread.Sleep(TimeSpan.FromMilliseconds(5));
                        message = ReadByDelimeter(stream, Delimiter);
                    }
                }
            }

            _stopped.SetResult(true);
        }

        private static string ReadByDelimeter(StreamReader sr, string delimiter)
        {
            var line = new StringBuilder();
            var matchIndex = 0;

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
