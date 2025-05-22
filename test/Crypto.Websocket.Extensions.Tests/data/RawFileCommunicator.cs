using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Binance.Client.Websocket.Communicator;
using Bitfinex.Client.Websocket.Communicator;
using Bitmex.Client.Websocket.Communicator;
using Coinbase.Client.Websocket.Communicator;
using Websocket.Client;

namespace Crypto.Websocket.Extensions.Tests.data
{
    public class RawFileCommunicator : IBitmexCommunicator, IBitfinexCommunicator,
        IBinanceCommunicator, ICoinbaseCommunicator
    {
        private readonly Subject<ResponseMessage> _messageReceivedSubject = new Subject<ResponseMessage>();

        public IObservable<ResponseMessage> MessageReceived => _messageReceivedSubject.AsObservable();
        public IObservable<ReconnectionInfo> ReconnectionHappened => Observable.Empty<ReconnectionInfo>();
        public IObservable<DisconnectionInfo> DisconnectionHappened => Observable.Empty<DisconnectionInfo>();

        public TimeSpan? ReconnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan? ErrorReconnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public TimeSpan? LostReconnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public string Name { get; set; }
        public bool IsStarted { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsReconnectionEnabled { get; set; }
        public bool IsTextMessageConversionEnabled { get; set; }
        public bool IsStreamDisposedAutomatically { get; set; }
        public ClientWebSocket NativeClient { get; }
        public Encoding MessageEncoding { get; set; }

        public string[] FileNames { get; set; }
        public string Delimiter { get; set; } = ";;";
        public Encoding Encoding { get; set; } = Encoding.UTF8;

        public virtual void Dispose()
        {

        }

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

        public bool Send(ReadOnlySequence<byte> message)
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

        public bool SendAsText(ReadOnlySequence<byte> message)
        {
            return true;
        }

        public Task Reconnect()
        {
            return Task.CompletedTask;
        }

        public Task ReconnectOrFail()
        {
            return Task.CompletedTask;
        }

        public void StreamFakeMessage(ResponseMessage message)
        {
        }

        public Uri Url { get; set; }

        private void StartStreaming()
        {
            if (FileNames == null)
                throw new InvalidOperationException("FileNames are not set, provide at least one path to historical data");
            if (string.IsNullOrEmpty(Delimiter))
                throw new InvalidOperationException("Delimiter is not set (separator between messages in the file)");

            foreach (var fileName in FileNames)
            {
                using var stream = GetFileStreamReader(fileName);
                var message = ReadByDelimeter(stream, Delimiter);
                while (message != null)
                {
                    _messageReceivedSubject.OnNext(ResponseMessage.TextMessage(message));
                    message = ReadByDelimeter(stream, Delimiter);
                }
            }
        }


        private static string ReadByDelimeter(StreamReader sr, string delimiter)
        {
            var line = new StringBuilder();
            int matchIndex = 0;
            var nextChar = (char)sr.Read();

            while (nextChar > 0 && nextChar < 65535)
            {
                line.Append(nextChar);
                if (nextChar == delimiter[matchIndex])
                {
                    if (matchIndex == delimiter.Length - 1)
                    {
                        return line.ToString().Substring(0, line.Length - delimiter.Length);
                    }
                    matchIndex++;
                }
                else
                {
                    matchIndex = 0;
                }
                nextChar = (char)sr.Read();
            }

            return line.Length == 0 ? null : line.ToString();
        }

        private StreamReader GetFileStreamReader(string fileName)
        {
            var fs = new FileStream(fileName, FileMode.Open);
            if (fileName.EndsWith("gz", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("gzip", StringComparison.OrdinalIgnoreCase))
            {
                return new StreamReader(new GZipStream(fs, CompressionMode.Decompress), Encoding);
            }

            return new StreamReader(fs, Encoding);
        }
    }
}
