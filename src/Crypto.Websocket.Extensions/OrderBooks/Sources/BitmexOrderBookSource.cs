using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Responses;
using Bitmex.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Logging;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.Threading;
using Crypto.Websocket.Extensions.Validations;
using Newtonsoft.Json;
using OrderBookLevel = Crypto.Websocket.Extensions.OrderBooks.Models.OrderBookLevel;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class BitmexOrderBookSource : OrderBookLevel2SourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly CryptoAsyncLock _locker = new CryptoAsyncLock();

        private readonly HttpClient _httpClient = new HttpClient();
        private BitmexWebsocketClient _client;
        private IDisposable _subscription;

        /// <inheritdoc />
        public BitmexOrderBookSource(BitmexWebsocketClient client)
        {
            _httpClient.BaseAddress = new Uri("https://www.bitmex.com");

            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitmex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitmexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        /// <inheritdoc />
        public override async Task LoadSnapshot(string pair, int count = 1000)
        {
            //using (await _locker.LockAsync())
            //{
                await LoadSnapshotInternal(pair, count);
            //}
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.BookStream.Subscribe(HandleBookResponse);
        }

        // too slow
        private void HandleBookResponseSynchronized(BookResponse bookResponse)
        {
            using (_locker.Lock())
            {
                HandleBookResponse(bookResponse);
            }
        }

        private void HandleBookResponse(BookResponse bookResponse)
        {
            if (bookResponse.Action == BitmexAction.Undefined)
            {
                // weird state, do nothing
                return;
            }

            if (bookResponse.Action == BitmexAction.Partial)
            {
                // received snapshot, convert and stream
                OrderBookSnapshotSubject.OnNext(ConvertLevels(bookResponse.Data));
                return;
            }

            // received difference, convert and stream

            var action = ConvertAction(bookResponse.Action);
            var bulk = new OrderBookLevelBulk(action, ConvertLevels(bookResponse.Data));

            OrderBookSubject.OnNext(bulk);
        }

        private OrderBookLevel[] ConvertLevels(BookLevel[] data)
        {
            return data
                .Select(x => new OrderBookLevel
                (
                    x.Id.ToString(),
                    ConvertSide(x.Side),
                    x.Price,
                    x.Size,
                    null,
                    x.Symbol
                ))
                .ToArray();
        }

        private CryptoSide ConvertSide(BitmexSide side)
        {
            switch (side)
            {
                case BitmexSide.Buy:
                    return CryptoSide.Bid;
                case BitmexSide.Sell:
                    return CryptoSide.Ask;
                default:
                    return CryptoSide.Undefined;
            }
        }

        private OrderBookAction ConvertAction(BitmexAction action)
        {
            switch (action)
            {
                case BitmexAction.Insert:
                    return OrderBookAction.Insert;
                case BitmexAction.Update:
                    return OrderBookAction.Update;
                case BitmexAction.Delete:
                    return OrderBookAction.Delete;
                default:
                    return OrderBookAction.Undefined;
            }
        }

        private async Task LoadSnapshotInternal(string pair, int count)
        {
            BookLevel[] parsed = null;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            var countSafe = count > 1000 ? 0 : count;

            try
            {
                var url = $"/api/v1/orderBook/L2?symbol={pairSafe}&depth={countSafe}";
                using (HttpResponseMessage response = await _httpClient.GetAsync(url))
                using (HttpContent content = response.Content)
                {
                    var result = await content.ReadAsStringAsync();
                    parsed = JsonConvert.DeserializeObject<BookLevel[]>(result);
                    if (parsed == null || !parsed.Any())
                        return;
                }
            }
            catch (Exception e)
            {
                Log.Trace($"[{ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                         $"Error: {e.Message}");
                return;
            }

            // received snapshot, convert and stream
            OrderBookSnapshotSubject.OnNext(ConvertLevels(parsed));
        }
    }
}
