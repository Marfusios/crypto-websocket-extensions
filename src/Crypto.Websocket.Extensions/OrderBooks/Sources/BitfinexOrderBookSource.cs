using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Responses;
using Bitfinex.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class BitfinexOrderBookSource : OrderBookSourceBase
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private BitfinexWebsocketClient _client = null!;
        private IDisposable? _subscription;
        private IDisposable? _subscriptionSnapshot;


        /// <inheritdoc />
        public BitfinexOrderBookSource(BitfinexWebsocketClient client) : base(client.Logger)
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

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.BookSnapshotStream.Subscribe(HandleSnapshot);
            _subscription = _client.Streams.BookStream.Subscribe(HandleBook);
        }

        private void HandleSnapshot(Book[] books)
        {
            // received snapshot, convert and stream
            var levels = ConvertLevels(books);
            var last = books.LastOrDefault();
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
            FillBulk(last, bulk);
            StreamSnapshot(bulk);
        }

        private void HandleBook(Book book)
        {
            BufferData(book);
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

        private CryptoOrderSide ConvertSide(double amount)
        {
            if (amount > 0)
                return CryptoOrderSide.Bid;
            if (amount < 0)
                return CryptoOrderSide.Ask;
            return CryptoOrderSide.Undefined;
        }

        private OrderBookAction RecognizeAction(Book book)
        {
            if (book.Count > 0)
                return OrderBookAction.Update;
            return OrderBookAction.Delete;
        }

        /// <inheritdoc />
        protected override async Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000)
        {
            Book[]? parsed = null;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            pairSafe = $"t{pairSafe}";
            var countSafe = count > 100 ? 100 : count;
            var result = string.Empty;

            try
            {
                var url = $"/v2/book/{pairSafe}/P0?len={countSafe}";
                using HttpResponseMessage response = await _httpClient.GetAsync(url);
                using HttpContent content = response.Content;

                result = await content.ReadAsStringAsync();
                parsed = JsonConvert.DeserializeObject<Book[]>(result);
                if (parsed == null || !parsed.Any())
                    return null;

                foreach (var book in parsed)
                {
                    book.Pair = pair ?? string.Empty;
                }
            }
            catch (Exception e)
            {
                _client.Logger.LogDebug("[ORDER BOOK {exchangeName}] Failed to load L2 orderbook snapshot for pair '{pair}'. " +
                         "Error: '{error}'.  Content: '{content}'", ExchangeName, pairSafe, e.Message, result);
                return null;
            }

            var levels = ConvertLevels(parsed);
            var last = parsed.LastOrDefault();
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
            FillBulk(last, bulk);
            return bulk;
        }

        private OrderBookLevelBulk ConvertDiff(Book book)
        {
            var converted = ConvertLevel(book);
            var action = RecognizeAction(book);
            var bulk = new OrderBookLevelBulk(action, new[] { converted }, CryptoOrderBookType.L2);
            FillBulk(book, bulk);
            return bulk;
        }

        private void FillBulk(ResponseBase? response, OrderBookLevelBulk bulk)
        {
            if (response == null)
                return;

            bulk.ExchangeName = ExchangeName;
            bulk.ServerTimestamp = response.ServerTimestamp;
            bulk.ServerSequence = response.ServerSequence;
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            var result = new List<OrderBookLevelBulk>();
            foreach (var response in data)
            {
                var responseSafe = response as Book;
                if (responseSafe == null)
                    continue;

                var converted = ConvertDiff(responseSafe);
                result.Add(converted);
            }

            return result.ToArray();
        }


    }
}
