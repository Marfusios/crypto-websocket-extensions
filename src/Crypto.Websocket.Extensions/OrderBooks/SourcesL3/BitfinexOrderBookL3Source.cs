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
using Crypto.Websocket.Extensions.Logging;
using Newtonsoft.Json;

namespace Crypto.Websocket.Extensions.OrderBooks.SourcesL3
{
    /// <inheritdoc />
    public class BitfinexOrderBookL3Source : OrderBookSourceBase
    {
        static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        readonly HttpClient _httpClient = new();
        IBitfinexPublicWebsocketClient _client;
        IDisposable _snapshotSubscription;
        IDisposable _diffSubscription;

        /// <inheritdoc />
        public BitfinexOrderBookL3Source(IBitfinexPublicWebsocketClient client)
        {
            _httpClient.BaseAddress = new Uri("https://api-pub.bitfinex.com");

            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitfinex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(IBitfinexPublicWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _snapshotSubscription?.Dispose();
            _diffSubscription?.Dispose();
            Subscribe();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();
            _snapshotSubscription?.Dispose();
            _diffSubscription?.Dispose();
        }

        void Subscribe()
        {
            _snapshotSubscription = _client.Streams.RawBookSnapshotStream.Subscribe(HandleSnapshot);
            _diffSubscription = _client.Streams.RawBookStream.Subscribe(HandleBook);
        }

        void HandleSnapshot(RawBook[] books)
        {
            // received snapshot, convert and stream
            var levels = ConvertLevels(books);
            var last = books.LastOrDefault();
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L3);
            FillBulk(last, bulk);
            StreamSnapshot(bulk);
        }

        void HandleBook(RawBook book)
        {
            BufferData(book);
        }

        OrderBookLevel[] ConvertLevels(RawBook[] data)
        {
            return data
                .Select(ConvertLevel)
                .ToArray();
        }

        OrderBookLevel ConvertLevel(RawBook x)
        {
            return new
            (
                x.OrderId.ToString(CultureInfo.InvariantCulture),
                ConvertSide(x.Amount),
                x.Price,
                x.Amount,
                null,
                x.Pair
            );
        }

        static CryptoOrderSide ConvertSide(double amount)
        {
            if (amount > 0)
                return CryptoOrderSide.Bid;
            if (amount < 0)
                return CryptoOrderSide.Ask;
            return CryptoOrderSide.Undefined;
        }

        static OrderBookAction RecognizeAction(RawBook book)
        {
            if (book.Price > 0)
                return OrderBookAction.Update;
            return OrderBookAction.Delete;
        }

        /// <inheritdoc />
        protected override async Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count = 1000)
        {
            RawBook[] parsed = null;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            pairSafe = $"t{pairSafe}";
            var countSafe = count > 100 ? 100 : count;
            var result = string.Empty;

            try
            {
                var url = $"/v2/book/{pairSafe}/R0?len={countSafe}";
                using var response = await _httpClient.GetAsync(url);
                using var content = response.Content;
                result = await content.ReadAsStringAsync();
                parsed = JsonConvert.DeserializeObject<RawBook[]>(result);
               
                if (parsed == null || !parsed.Any())
                    return null;

                foreach (var book in parsed)
                    book.Pair = pair;
            }
            catch (Exception e)
            {
                Log.Debug($"[ORDER BOOK {ExchangeName}] Failed to load L3 orderbook snapshot for pair '{pairSafe}'. " +
                         $"Error: '{e.Message}'.  Content: '{result}'");
                return null;
            }

            var levels = ConvertLevels(parsed);
            var last = parsed.LastOrDefault();
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L3);
            FillBulk(last, bulk);
            return bulk;
        }

        OrderBookLevelBulk ConvertDiff(RawBook book)
        {
            var converted = ConvertLevel(book);
            var action = RecognizeAction(book);
            var bulk = new OrderBookLevelBulk(action, new[] {converted}, CryptoOrderBookType.L3);
            FillBulk(book, bulk);
            return bulk;
        }

        void FillBulk(ResponseBase response, OrderBookLevelBulk bulk)
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
                if (response is RawBook responseSafe)
                {
                    var converted = ConvertDiff(responseSafe);
                    result.Add(converted);
                }
            }

            return result.ToArray();
        }
    }
}
