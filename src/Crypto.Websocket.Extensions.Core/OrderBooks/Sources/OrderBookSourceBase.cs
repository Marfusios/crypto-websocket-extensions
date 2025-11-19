using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
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
        private readonly SemaphoreSlim _snapshotLocker = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private readonly Queue<object> _dataBuffer = new Queue<object>();
        private bool _bufferEnabled = true;
        private PeriodicTimer _timer;
        private TimeSpan _bufferInterval = TimeSpan.FromMilliseconds(10);

        /// <summary>
        /// Use this subject to stream order book snapshot data
        /// </summary>
        private readonly Subject<OrderBookLevelBulk> _orderBookSnapshotSubject = new Subject<OrderBookLevelBulk>();

        /// <summary>
        /// Use this subject to stream order book data (level difference)
        /// </summary>
        private readonly Subject<OrderBookLevelBulk[]> _orderBookSubject = new Subject<OrderBookLevelBulk[]>();

        /// <summary>
        /// Hidden constructor
        /// </summary>
        protected OrderBookSourceBase(ILogger logger)
        {
            _logger = logger;
            _timer = new PeriodicTimer(_bufferInterval);
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
        public TimeSpan BufferInterval
        {
            get => _bufferInterval;
            set
            {
                if (_bufferInterval == value)
                    return;

                if (value == TimeSpan.Zero)
                    throw new ArgumentException("Buffer interval cannot be zero.", nameof(value));

                _bufferInterval = value;

                // Recreate timer with new interval
                var oldTimer = Interlocked.Exchange(ref _timer, new PeriodicTimer(value));
                oldTimer.Dispose();
            }
        }

        /// <inheritdoc />
        public IObservable<OrderBookLevelBulk> OrderBookSnapshotStream => _orderBookSnapshotSubject.AsObservable();

        /// <inheritdoc />
        public IObservable<OrderBookLevelBulk[]> OrderBookStream => _orderBookSubject.AsObservable();

        /// <inheritdoc />
        public async Task LoadSnapshot(string pair, int count = 1000)
        {
            await _snapshotLocker.WaitAsync();
            try
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
            finally
            {
                _snapshotLocker.Release();
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
            if (!_bufferEnabled)
            {
                ConvertAndStream(new[] { data });
                return;
            }

            lock (_bufferLocker)
            {
                _dataBuffer.Enqueue(data);
            }
        }

        /// <summary>
        /// Convert received data into output bulk object
        /// </summary>
        protected abstract OrderBookLevelBulk[] ConvertData(object[] data);

        private void StartProcessingFromBufferThread()
        {
            Task.Factory.StartNew(_ => ProcessData(),
                _cancellation.Token,
                TaskCreationOptions.LongRunning);
        }

        private async Task ProcessData()
        {
            while (!_cancellation.IsCancellationRequested && _bufferEnabled)
            {
                try
                {
                    if (!await _timer.WaitForNextTickAsync(_cancellation.Token))
                        break;

                    StreamDataSynchronized();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception e)
                {
                    _logger.LogDebug(e, "[{exchangeName}] Failed while buffering orderbook changes.", ExchangeName);
                }
            }
        }

        private void StreamDataSynchronized()
        {
            if (LoadSnapshotEnabled)
            {
                _snapshotLocker.Wait();
                try
                {
                    StreamData();
                }
                finally
                {
                    _snapshotLocker.Release();
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
                    // no message in buffer, do nothing
                    return;
                }
            }

            ConvertAndStream(data);
        }

        private void ConvertAndStream(object[] dataArr)
        {
            var converted = ConvertData(dataArr);
            _orderBookSubject.OnNext(converted);
        }
    }
}
