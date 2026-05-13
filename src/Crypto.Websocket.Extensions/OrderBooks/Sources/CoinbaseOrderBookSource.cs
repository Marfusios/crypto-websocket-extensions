using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Responses;
using Coinbase.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrderBookLevel = Crypto.Websocket.Extensions.Core.OrderBooks.Models.OrderBookLevel;
using CoinbaseOrderBookLevel = Coinbase.Client.Websocket.Responses.Books.OrderBookLevel;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class CoinbaseOrderBookSource : OrderBookSourceBase
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private CoinbaseWebsocketClient _client = null!;
        private IDisposable? _subscription;
        private IDisposable? _subscriptionSnapshot;


        /// <inheritdoc />
        public CoinbaseOrderBookSource(CoinbaseWebsocketClient client) : base(client.Logger)
        {
            _httpClient.BaseAddress = new Uri("https://api.pro.coinbase.com");

            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "coinbase";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(CoinbaseWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.OrderBookSnapshotStream.Subscribe(HandleSnapshot);
            _subscription = _client.Streams.OrderBookUpdateStream.Subscribe(HandleBook);
        }

        private void HandleSnapshot(OrderBookSnapshotResponse snapshot)
        {
            // received snapshot, convert and stream
            var levels = ConvertSnapshot(snapshot);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
            FillBulk(snapshot, bulk);
            StreamSnapshot(bulk);
        }

        private OrderBookLevel[] ConvertSnapshot(OrderBookSnapshotResponse snapshot)
        {
            return ConvertLevels(snapshot.ProductId, snapshot.Bids, snapshot.Asks);
        }

        private void HandleBook(OrderBookUpdateResponse update)
        {
            BufferData(update);
        }

        private OrderBookLevel[] ConvertLevels(string? pair, CoinbaseOrderBookLevel[] data)
        {
            var result = new OrderBookLevel[data.Length];
            for (var index = 0; index < data.Length; index++)
                result[index] = ConvertLevel(pair, data[index]);

            return result;
        }

        private OrderBookLevel ConvertLevel(string? pair, CoinbaseOrderBookLevel x)
        {
            return new OrderBookLevel
            (
                x.Price.ToString(CultureInfo.InvariantCulture),
                ConvertSide(x.Side),
                x.Price,
                x.Amount,
                null,
                pair
            );
        }

        private CryptoOrderSide ConvertSide(OrderBookSide side)
        {
            if (side == OrderBookSide.Buy)
                return CryptoOrderSide.Bid;
            if (side == OrderBookSide.Sell)
                return CryptoOrderSide.Ask;
            return CryptoOrderSide.Undefined;
        }

        private OrderBookAction RecognizeAction(OrderBookLevel level)
        {
            if (level.Amount > 0)
                return OrderBookAction.Update;
            return OrderBookAction.Delete;
        }

        /// <inheritdoc />
        protected override async Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000)
        {
            OrderBookSnapshotDto? parsed;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpperInvariant();
            var result = string.Empty;

            try
            {
                var url = $"/products/{pairSafe}/book?level=2";
                using HttpResponseMessage response = await _httpClient.GetAsync(url);
                using HttpContent content = response.Content;

                result = await content.ReadAsStringAsync();
                parsed = JsonConvert.DeserializeObject<OrderBookSnapshotDto>(result);
                if (parsed == null)
                    return null;
            }
            catch (Exception e)
            {
                _client.Logger.LogDebug("[ORDER BOOK {exchangeName}] Failed to load orderbook snapshot for pair '{pair}'. " +
                         "Error: '{error}'.  Content: '{content}'", ExchangeName, pairSafe, e.Message, result);
                return null;
            }

            var levels = ConvertLevels(pair, parsed.Bids, parsed.Asks);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
            return bulk;
        }

        private OrderBookLevelBulk[] ConvertDiff(OrderBookUpdateResponse update)
        {
            if (update.Changes.Length == 0)
                return Array.Empty<OrderBookLevelBulk>();

            var toDelete = new OrderBookLevel[update.Changes.Length];
            var toUpdate = new OrderBookLevel[update.Changes.Length];
            var deleteCount = 0;
            var updateCount = 0;

            foreach (var change in update.Changes)
            {
                var level = ConvertLevel(update.ProductId, change);
                if (RecognizeAction(level) == OrderBookAction.Delete)
                    toDelete[deleteCount++] = level;
                else
                    toUpdate[updateCount++] = level;
            }

            var result = new OrderBookLevelBulk[2];
            var resultCount = 0;

            if (deleteCount > 0)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, TrimLevels(toDelete, deleteCount), CryptoOrderBookType.L2);
                FillBulk(update, bulk);
                result[resultCount++] = bulk;
            }

            if (updateCount > 0)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Update, TrimLevels(toUpdate, updateCount), CryptoOrderBookType.L2);
                FillBulk(update, bulk);
                result[resultCount++] = bulk;
            }

            return TrimBulks(result, resultCount);
        }

        private void FillBulk(ResponseBase response, OrderBookLevelBulk bulk)
        {
            bulk.ExchangeName = ExchangeName;
            bulk.ServerSequence = response.Sequence;
            bulk.ServerTimestamp = response.Time;
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object data)
        {
            return data is OrderBookUpdateResponse response
                ? ConvertDiff(response)
                : Array.Empty<OrderBookLevelBulk>();
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            if (data.Length == 0)
                return Array.Empty<OrderBookLevelBulk>();

            var result = new OrderBookLevelBulk[data.Length * 2];
            var count = 0;
            foreach (var response in data)
            {
                var responseSafe = response as OrderBookUpdateResponse;
                if (responseSafe == null)
                    continue;

                var converted = ConvertDiff(responseSafe);
                for (var index = 0; index < converted.Length; index++)
                    result[count++] = converted[index];
            }

            return TrimBulks(result, count);
        }

        private OrderBookLevel[] ConvertLevels(string? pair, CoinbaseOrderBookLevel[] bids, CoinbaseOrderBookLevel[] asks)
        {
            var result = new OrderBookLevel[bids.Length + asks.Length];
            var index = 0;

            for (var bidIndex = 0; bidIndex < bids.Length; bidIndex++)
                result[index++] = ConvertLevel(pair, bids[bidIndex]);

            for (var askIndex = 0; askIndex < asks.Length; askIndex++)
                result[index++] = ConvertLevel(pair, asks[askIndex]);

            return result;
        }

        private static OrderBookLevel[] TrimLevels(OrderBookLevel[] levels, int count)
        {
            if (count == 0)
                return Array.Empty<OrderBookLevel>();

            if (count == levels.Length)
                return levels;

            Array.Resize(ref levels, count);
            return levels;
        }

        private static OrderBookLevelBulk[] TrimBulks(OrderBookLevelBulk[] bulks, int count)
        {
            if (count == 0)
                return Array.Empty<OrderBookLevelBulk>();

            if (count == bulks.Length)
                return bulks;

            Array.Resize(ref bulks, count);
            return bulks;
        }

        // ReSharper disable once ClassNeverInstantiated.Local
        private class OrderBookSnapshotDto
        {
            public long Sequence { get; set; }

            [JsonConverter(typeof(OrderBookLevelConverter), OrderBookSide.Buy)]
            public CoinbaseOrderBookLevel[] Bids { get; set; } = Array.Empty<CoinbaseOrderBookLevel>();

            [JsonConverter(typeof(OrderBookLevelConverter), OrderBookSide.Sell)]
            public CoinbaseOrderBookLevel[] Asks { get; set; } = Array.Empty<CoinbaseOrderBookLevel>();
        }
    }


    internal class OrderBookLevelConverter : JsonConverter
    {
        private readonly OrderBookSide _side;

        public OrderBookLevelConverter()
        {

        }

        public OrderBookLevelConverter(OrderBookSide side)
        {
            _side = side;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(double[][]);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue,
            JsonSerializer serializer)
        {
            var array = JArray.Load(reader);
            return JArrayToTradingTicker(array);
        }

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        private CoinbaseOrderBookLevel[] JArrayToTradingTicker(JArray data)
        {
            var result = new CoinbaseOrderBookLevel[data.Count];
            for (var index = 0; index < data.Count; index++)
            {
                var item = data[index];

                var level = new CoinbaseOrderBookLevel();
                level.Price = (double)item[0]!;
                level.Amount = (double)item[1]!;
                level.Side = _side;

                result[index] = level;
            }

            return result;
        }
    }
}
