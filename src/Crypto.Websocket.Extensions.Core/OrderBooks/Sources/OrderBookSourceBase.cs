using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.Logging;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.Threading;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Sources
{
    /// <inheritdoc />
    public abstract class OrderBookSourceBase : IOrderBookSource
    {
        static readonly ILog LogBase = LogProvider.GetCurrentClassLogger();

        readonly object _bufferLocker = new();
        readonly CryptoAsyncLock _snapshotLocker = new();
        readonly CancellationTokenSource _cancellation = new();

        readonly Queue<object> _dataBuffer = new();
        bool _bufferEnabled = true;
        readonly ManualResetEvent _bufferPauseEvent = new(false);

        /// <summary>
        /// Use this subject to stream order book snapshot data
        /// </summary>
        readonly Subject<OrderBookLevelBulk> _orderBookSnapshotSubject = new();

        /// <summary>
        /// Use this subject to stream order book data (level difference)
        /// </summary>
        readonly Subject<OrderBookLevelBulk[]> _orderBookSubject = new();

        /// <summary>
        /// Hidden constructor
        /// </summary>
        protected OrderBookSourceBase()
        {
            StartProcessingFromBufferThread();
        }

        /// <summary>
        /// Dispose background processing
        /// </summary>
        public virtual void Dispose()
        {
            _cancellation.Cancel();
        }

        /// <inheritdoc />
        public abstract string ExchangeName { get; }

        /// <inheritdoc />
        public bool LoadSnapshotEnabled { get; set; } = false;

        /// <inheritdoc />
        public bool BufferEnabled
        {
            get => _bufferEnabled;
            set
            {
                var wasDisabled = !_bufferEnabled;
                _bufferEnabled = value;

                if (wasDisabled && _bufferEnabled)
                {
                    // buffering was disabled, enable it
                    StartProcessingFromBufferThread();
                }
            }
        }

        /// <inheritdoc />
        public TimeSpan BufferInterval { get; set; } = TimeSpan.FromMilliseconds(10);

        /// <inheritdoc />
        public IObservable<OrderBookLevelBulk> OrderBookSnapshotStream => _orderBookSnapshotSubject.AsObservable();

        /// <inheritdoc />
        public IObservable<OrderBookLevelBulk[]> OrderBookStream => _orderBookSubject.AsObservable();

        /// <inheritdoc />
        public async Task LoadSnapshot(string pair, int count = 1000)
        {
            using (await _snapshotLocker.LockAsync())
            {
                OrderBookLevelBulk data = null;
                try
                {
                    data = await LoadSnapshotInternal(pair, count);
                }
                catch (Exception e)
                {
                    LogBase.Debug($"[{ExchangeName}] Failed to load orderbook snapshot for pair '{pair}'. " +
                              $"Error: {e.Message}");
                }

                StreamSnapshot(data);
            }
        }

        /// <summary>
        /// Returns true if order book is in valid state.
        /// Should be overriden by specific exchange implementation.
        /// </summary>
        public virtual bool IsValid()
        {
            return true;
        }

        /// <summary>
        /// Implement snapshot loading, it should not throw an exception
        /// </summary>
        protected abstract Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count = 1000);

        /// <summary>
        /// Check null and empty, then stream snapshot
        /// </summary>
        protected void StreamSnapshot(OrderBookLevelBulk data)
        {
            if (data?.Levels != null && data.Levels.Any())
            {
                _orderBookSnapshotSubject.OnNext(data);
            }
        }

        /// <summary>
        /// Save received data into the buffer or
        /// stream directly if buffering is disabled
        /// </summary>
        protected void BufferData(object data)
        {
            if (_bufferEnabled)
            {
                lock (_bufferLocker)
                {
                    _dataBuffer.Enqueue(data);
                }

                // unblock waiting
                _bufferPauseEvent.Set();
                return;
            }
            
            ConvertAndStream(new []{data});
        }

        /// <summary>
        /// Convert received data into output bulk objects
        /// </summary>
        protected abstract OrderBookLevelBulk[] ConvertData(object[] data);

        void StartProcessingFromBufferThread()
        {
            Task.Factory.StartNew(_ => ProcessData(), 
                _cancellation.Token, 
                TaskCreationOptions.LongRunning);
        }

        async Task ProcessData()
        {
            var bufferIntervalMs = BufferInterval.TotalMilliseconds;

            while (!_cancellation.IsCancellationRequested && _bufferEnabled)
            {
                if (bufferIntervalMs > 0)
                {
                    // delay only if enabled
                    await Task.Delay(BufferInterval);
                }

                // wait when there is no message
                _bufferPauseEvent.WaitOne();

                StreamDataSynchronized();
            }
        }

        void StreamDataSynchronized()
        {
            if (LoadSnapshotEnabled)
            {
                using (_snapshotLocker.Lock())
                {
                    StreamData();
                }
            }
            else
            {
                StreamData();
            }
        }

        void StreamData()
        {
            object[] data;
            lock (_bufferLocker)
            {
                data = _dataBuffer.ToArray();
                _dataBuffer.Clear();

                if (data.Length <= 0)
                {
                    // no message in buffer, enable waiting
                    _bufferPauseEvent.Reset();
                    return;
                }
            }

            ConvertAndStream(data);
        }

        void ConvertAndStream(object[] dataArr)
        {
            var converted = ConvertData(dataArr);
            _orderBookSubject.OnNext(converted);
        }
    }
}
