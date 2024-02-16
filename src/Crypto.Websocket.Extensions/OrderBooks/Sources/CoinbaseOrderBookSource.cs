using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
            var bids = ConvertLevels(snapshot.ProductId, snapshot.Bids);
            var asks = ConvertLevels(snapshot.ProductId, snapshot.Asks);
            var levels = bids.Concat(asks).ToArray();
            return levels;
        }

        private void HandleBook(OrderBookUpdateResponse update)
        {
            BufferData(update);
        }

        private OrderBookLevel[] ConvertLevels(string pair, CoinbaseOrderBookLevel[] data)
        {
            return data
                .Select(x => ConvertLevel(pair, x))
                .ToArray();
        }

        private OrderBookLevel ConvertLevel(string pair, CoinbaseOrderBookLevel x)
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
            OrderBookSnapshotDto? parsed = null;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
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

            var bids = ConvertLevels(pair, parsed.Bids);
            var asks = ConvertLevels(pair, parsed.Asks);
            var levels = bids.Concat(asks).ToArray();
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
            return bulk;
        }

        private IEnumerable<OrderBookLevelBulk> ConvertDiff(OrderBookUpdateResponse update)
        {
            var converted = ConvertLevels(update.ProductId, update.Changes);

            var group = converted.GroupBy(RecognizeAction).ToArray();

            foreach (var actionGroup in group)
            {
                var bulk = new OrderBookLevelBulk(actionGroup.Key, actionGroup.ToArray(), CryptoOrderBookType.L2);
                FillBulk(update, bulk);
                yield return bulk;
            }
        }

        private void FillBulk(ResponseBase response, OrderBookLevelBulk bulk)
        {
            bulk.ExchangeName = ExchangeName;
            bulk.ServerSequence = response.Sequence;
            bulk.ServerTimestamp = response.Time;
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            var result = new List<OrderBookLevelBulk>();
            foreach (var response in data)
            {
                var responseSafe = response as OrderBookUpdateResponse;
                if (responseSafe == null)
                    continue;

                var converted = ConvertDiff(responseSafe);
                result.AddRange(converted);
            }

            return result.ToArray();
        }

        // ReSharper disable once ClassNeverInstantiated.Local
        private class OrderBookSnapshotDto
        {
            public long Sequence { get; set; }

            [JsonConverter(typeof(OrderBookLevelConverter), OrderBookSide.Buy)]
            public CoinbaseOrderBookLevel[] Bids { get; set; }

            [JsonConverter(typeof(OrderBookLevelConverter), OrderBookSide.Sell)]
            public CoinbaseOrderBookLevel[] Asks { get; set; }
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

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            var array = JArray.Load(reader);
            return JArrayToTradingTicker(array);
        }

        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        private CoinbaseOrderBookLevel[] JArrayToTradingTicker(JArray data)
        {
            var result = new List<CoinbaseOrderBookLevel>();
            foreach (var item in data)
            {
                var array = item.ToArray();

                var level = new CoinbaseOrderBookLevel();
                level.Price = (double)array[0];
                level.Amount = (double)array[1];
                level.Side = _side;

                result.Add(level);
            }

            return result.ToArray();
        }
    }
}
