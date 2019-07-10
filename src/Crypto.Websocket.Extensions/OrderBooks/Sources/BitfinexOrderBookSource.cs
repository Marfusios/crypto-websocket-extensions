using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Logging;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.Threading;
using Crypto.Websocket.Extensions.Validations;
using Newtonsoft.Json;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class BitfinexOrderBookSource : OrderBookLevel2SourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly CryptoAsyncLock _locker = new CryptoAsyncLock();

        private readonly HttpClient _httpClient = new HttpClient();
        private BitfinexWebsocketClient _client;
        private IDisposable _subscription;
        private IDisposable _subscriptionSnapshot;


        /// <inheritdoc />
        public BitfinexOrderBookSource(BitfinexWebsocketClient client)
        {
            _httpClient.BaseAddress = new Uri("https://api-pub.bitfinex.com");

            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitfinex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitfinexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
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
            _subscriptionSnapshot = _client.Streams.BookSnapshotStream.Subscribe(HandleSnapshot);
            _subscription = _client.Streams.BookStream.Subscribe(HandleBook);
        }

        private void HandleSnapshot(Book[] books)
        {
            // received snapshot, convert and stream
            OrderBookSnapshotSubject.OnNext(ConvertLevels(books));
        }

        // too slow
        private void HandleBookSynchronized(Book book)
        {
            using (_locker.Lock())
            {
                HandleBook(book);
            }
        }

        private void HandleBook(Book book)
        {
            var converted = ConvertLevel(book);
            var action = RecognizeAction(book);
            var bulk = new OrderBookLevelBulk(action, new[] {converted});
            OrderBookSubject.OnNext(bulk);
        }

        private OrderBookLevel[] ConvertLevels(Book[] data)
        {
            return data
                .Select(ConvertLevel)
                .ToArray();
        }

        private OrderBookLevel ConvertLevel(Book x)
        {
            return new OrderBookLevel
            (
                x.Price.ToString(CultureInfo.InvariantCulture),
                ConvertSide(x.Amount),
                x.Price,
                x.Amount,
                x.Count,
                x.Pair
            );
        }

        private CryptoSide ConvertSide(double amount)
        {
            if (amount > 0)
                return CryptoSide.Bid;
            if (amount < 0)
                return CryptoSide.Ask;
            return CryptoSide.Undefined;
        }

        private OrderBookAction RecognizeAction(Book book)
        {
            if (book.Count > 0)
                return OrderBookAction.Update;
            return OrderBookAction.Delete;
        }

        private async Task LoadSnapshotInternal(string pair, int count)
        {
            Book[] parsed = null;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            pairSafe = $"t{pairSafe}";
            var countSafe = count > 100 ? 100 : count;

            try
            {
                var url = $"/v2/book/{pairSafe}/P0?len={countSafe}";
                using (HttpResponseMessage response = await _httpClient.GetAsync(url))
                using (HttpContent content = response.Content)
                {
                    var result = await content.ReadAsStringAsync();
                    parsed = JsonConvert.DeserializeObject<Book[]>(result);
                    if (parsed == null || !parsed.Any())
                        return;

                    foreach (var book in parsed)
                    {
                        book.Pair = pair;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Trace($"[{ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                         $"Error: {e.Message}");
                return;
            }

            HandleSnapshot(parsed);
        }
    }
}
