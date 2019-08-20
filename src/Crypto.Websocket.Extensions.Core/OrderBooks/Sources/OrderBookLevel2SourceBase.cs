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
    public abstract class OrderBookLevel2SourceBase : IOrderBookLevel2Source
    {
        private static readonly ILog LogBase = LogProvider.GetCurrentClassLogger();

        private readonly object _bufferLocker = new object();
        private readonly CryptoAsyncLock _locker = new CryptoAsyncLock();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private readonly Queue<object> _dataBuffer = new Queue<object>();
        private bool _bufferEnabled = true;

        /// <summary>
        /// Use this subject to stream order book snapshot data
        /// </summary>
        private readonly Subject<OrderBookLevel[]> _orderBookSnapshotSubject = new Subject<OrderBookLevel[]>();

        /// <summary>
        /// Use this subject to stream order book data (level difference)
        /// </summary>
        private readonly Subject<OrderBookLevelBulk[]> _orderBookSubject = new Subject<OrderBookLevelBulk[]>();

        /// <inheritdoc />
        protected OrderBookLevel2SourceBase()
        {
            StartProcessingFromBufferThread();
        }

        /// <summary>
        /// Dispose background processing
        /// </summary>
        public void Dispose()
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
        public IObservable<OrderBookLevel[]> OrderBookSnapshotStream => _orderBookSnapshotSubject.AsObservable();

        /// <inheritdoc />
        public IObservable<OrderBookLevelBulk[]> OrderBookStream => _orderBookSubject.AsObservable();

        /// <inheritdoc />
        public async Task LoadSnapshot(string pair, int count = 1000)
        {
            using (await _locker.LockAsync())
            {
                OrderBookLevel[] data = null;
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
        protected abstract Task<OrderBookLevel[]> LoadSnapshotInternal(string pair, int count = 1000);

        /// <summary>
        /// Check null and empty, then stream snapshot
        /// </summary>
        protected void StreamSnapshot(OrderBookLevel[] data)
        {
            if (data != null && data.Any())
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
                return;
            }
            
            ConvertAndStream(new []{data});
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
                await Task.Delay(BufferInterval);
                StreamDataSynchronized();
            }
        }

        private void StreamDataSynchronized()
        {
            if (LoadSnapshotEnabled)
            {
                using (_locker.Lock())
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
