using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.Threading;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Sources
{
    /// <inheritdoc />
    public abstract class OrderBookSourceBase : IOrderBookSource
    {
        private readonly ILogger _logger;
#if NET9_0_OR_GREATER
        private readonly Lock _bufferLocker = new Lock();
#else
        private readonly object _bufferLocker = new object();
#endif
        private readonly CryptoAsyncLock _snapshotLocker = new CryptoAsyncLock();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private readonly Queue<object> _dataBuffer = new Queue<object>();
        private bool _bufferEnabled = true;
        private readonly ManualResetEvent _bufferPauseEvent = new ManualResetEvent(false);

        /// <summary>
        /// Use this subject to stream order book snapshot data
        /// </summary>
        private readonly Subject<OrderBookLevelBulk> _orderBookSnapshotSubject = new Subject<OrderBookLevelBulk>();

        /// <summary>
        /// Use this subject to stream order book data (level difference)
        /// </summary>
        private readonly Subject<OrderBookLevelBulk[]> _orderBookSubject = new Subject<OrderBookLevelBulk[]>();

        private Action<OrderBookLevelBulk>? _orderBookSingleHandlers;
        private Action<OrderBookLevelBulk[]>? _orderBookBatchHandlers;

        /// <summary>
        /// Hidden constructor
        /// </summary>
        protected OrderBookSourceBase(ILogger logger)
        {
            _logger = logger;
            StartProcessingFromBufferThread();
        }

        /// <summary>
        /// Exposed logger
        /// </summary>
        public ILogger Logger => _logger;

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

        /// <summary>
        /// If source provides only snapshots with no diffs, this will return false
        /// </summary>
        public virtual bool DiffsSupported { get; } = true;

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

        /// <summary>
        /// Subscribes an order book to the internal allocation-aware stream.
        /// </summary>
        internal IDisposable SubscribeOrderBookChanges(Action<OrderBookLevelBulk> onSingleBulk, Action<OrderBookLevelBulk[]> onBulkBatch)
        {
            lock (_bufferLocker)
            {
                _orderBookSingleHandlers += onSingleBulk;
                _orderBookBatchHandlers += onBulkBatch;
            }

            return new OrderBookChangeSubscription(this, onSingleBulk, onBulkBatch);
        }

        /// <inheritdoc />
        public async Task LoadSnapshot(string pair, int count = 1000)
        {
            using (await _snapshotLocker.LockAsync())
            {
                OrderBookLevelBulk? data = null;
                try
                {
                    data = await LoadSnapshotInternal(pair, count);
                }
                catch (Exception e)
                {
                    _logger.LogDebug("[{exchangeName}] Failed to load orderbook snapshot for pair '{pair}'. " +
                                  "Error: {error}", ExchangeName, pair, e.Message);
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
        protected abstract Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000);

        /// <summary>
        /// Check null and empty, then stream snapshot
        /// </summary>
        protected void StreamSnapshot(OrderBookLevelBulk? data)
        {
            if (data?.Levels is { Length: > 0 })
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

            ConvertAndStream(data);
        }

        /// <summary>
        /// Convert received data into output bulk object
        /// </summary>
        protected abstract OrderBookLevelBulk[] ConvertData(object[] data);

        /// <summary>
        /// Convert a single received item into output bulk objects.
        /// </summary>
        protected virtual OrderBookLevelBulk[] ConvertData(object data)
        {
            if (TryConvertData(data, out var bulk) && bulk != null)
                return new[] { bulk };

            return ConvertData(new[] { data });
        }

        /// <summary>
        /// Convert a single received item into one output bulk without allocating an outer bulk array.
        /// </summary>
        protected virtual bool TryConvertData(object data, out OrderBookLevelBulk? bulk)
        {
            bulk = null;
            return false;
        }

        private void StartProcessingFromBufferThread()
        {
            Task.Factory.StartNew(_ => ProcessData(),
                _cancellation.Token,
                TaskCreationOptions.LongRunning);
        }

        private async Task ProcessData()
        {
            var bufferIntervalMs = BufferInterval.TotalMilliseconds;

            while (!_cancellation.IsCancellationRequested && _bufferEnabled)
            {
                try
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
                catch (Exception e)
                {
                    _logger.LogDebug("[{exchangeName}] Failed while buffering orderbook changes." +
                                     "Error: {error}", ExchangeName, e.Message);
                }
            }
        }

        private void StreamDataSynchronized()
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

        private void StreamData()
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

        private void ConvertAndStream(object[] dataArr)
        {
            var converted = ConvertData(dataArr);
            StreamBatch(converted);
        }

        private void ConvertAndStream(object data)
        {
            if (TryConvertData(data, out var bulk) && bulk != null)
            {
                StreamSingle(bulk);
                return;
            }

            var converted = ConvertData(data);
            StreamBatch(converted);
        }

        private void StreamSingle(OrderBookLevelBulk bulk)
        {
            _orderBookSingleHandlers?.Invoke(bulk);

            if (_orderBookSubject.HasObservers)
                _orderBookSubject.OnNext(new[] { bulk });
        }

        private void StreamBatch(OrderBookLevelBulk[] converted)
        {
            _orderBookBatchHandlers?.Invoke(converted);

            if (_orderBookSubject.HasObservers)
                _orderBookSubject.OnNext(converted);
        }

        private void UnsubscribeOrderBookChanges(Action<OrderBookLevelBulk> onSingleBulk, Action<OrderBookLevelBulk[]> onBulkBatch)
        {
            lock (_bufferLocker)
            {
                _orderBookSingleHandlers -= onSingleBulk;
                _orderBookBatchHandlers -= onBulkBatch;
            }
        }

        private sealed class OrderBookChangeSubscription : IDisposable
        {
            private OrderBookSourceBase? _source;
            private readonly Action<OrderBookLevelBulk> _onSingleBulk;
            private readonly Action<OrderBookLevelBulk[]> _onBulkBatch;

            public OrderBookChangeSubscription(
                OrderBookSourceBase source,
                Action<OrderBookLevelBulk> onSingleBulk,
                Action<OrderBookLevelBulk[]> onBulkBatch)
            {
                _source = source;
                _onSingleBulk = onSingleBulk;
                _onBulkBatch = onBulkBatch;
            }

            public void Dispose()
            {
                var source = Interlocked.Exchange(ref _source, null);
                source?.UnsubscribeOrderBookChanges(_onSingleBulk, _onBulkBatch);
            }
        }
    }
}
