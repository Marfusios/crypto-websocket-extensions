using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Logging;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.Threading;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public abstract class OrderBookLevel2SourceBase : IOrderBookLevel2Source
    {
        private static readonly ILog LogBase = LogProvider.GetCurrentClassLogger();
        private readonly CryptoAsyncLock _locker = new CryptoAsyncLock();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

        private List<object> _dataBuffer = new List<object>();

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
            Task.Factory.StartNew(_ => ProcessData(), _cancellation.Token, TaskCreationOptions.LongRunning);
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
        public TimeSpan BufferInterval { get; set; } = TimeSpan.FromMilliseconds(100);

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
                    LogBase.Trace($"[{ExchangeName}] Failed to load orderbook snapshot for pair '{pair}'. " +
                              $"Error: {e.Message}");
                }

                StreamSnapshot(data);
            }
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
        /// Save received data into the buffer
        /// </summary>
        protected void BufferData(object data)
        {
            _dataBuffer.Add(data);
        }

        /// <summary>
        /// Convert received data into output bulk object
        /// </summary>
        protected abstract OrderBookLevelBulk[] ConvertData(object[] data);

        private async Task ProcessData()
        {
            while (!_cancellation.IsCancellationRequested)
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
            var data = _dataBuffer;
            _dataBuffer = new List<object>();

            var converted = ConvertData(data.ToArray());
            _orderBookSubject.OnNext(converted);
        }
    }
}
