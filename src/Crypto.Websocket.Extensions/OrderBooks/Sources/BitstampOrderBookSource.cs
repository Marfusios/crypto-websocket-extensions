using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Bitstamp.Client.Websocket.Client;
using Bitstamp.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class BitstampOrderBookSource : OrderBookSourceBase
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private BitstampWebsocketClient _client = null!;
        private IDisposable? _subscription;
        private IDisposable? _subscriptionSnapshot;

        /// <inheritdoc />
        public BitstampOrderBookSource(BitstampWebsocketClient client) : base(client.Logger)
        {
            _httpClient.BaseAddress = new Uri("https://www.bitstamp.net/api/");

            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitstamp";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitstampWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            _subscriptionSnapshot?.Dispose();
            Subscribe();
        }

        /// <summary>
        /// Request a new order book snapshot, will be handled without locking
        /// Method doesn't throw exception, just logs it
        /// </summary>
        /// <param name="pair">Target pair</param>
        /// <param name="count">Max level count</param>
        public async Task LoadSnapshotWithoutLock(string pair, int count = 1000)
        {
            var snapshot = await LoadSnapshotRaw(pair, count);
            if (snapshot == null)
                return;

            HandleSnapshot(snapshot);
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.OrderBookDiffStream.Subscribe(HandleDiff);
            _subscriptionSnapshot = _client.Streams.OrderBookStream.Subscribe(HandleSnapshot);
        }

        private void HandleSnapshot(OrderBookResponse response)
        {
            // received snapshot, convert and stream
            var levels = ConvertSnapshot(response);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2)
            {
                ExchangeName = ExchangeName,
                ServerTimestamp = response.Data.Microtimestamp
            };
            StreamSnapshot(bulk);
        }

        private void HandleDiff(OrderBookDiffResponse response)
        {
            BufferData(response);
        }

        private OrderBookLevel[] ConvertLevels(BookLevel[]? books, string? pair, CryptoOrderSide side)
        {
            if (books == null)
                return Array.Empty<OrderBookLevel>();

            return books
                .Select(x => ConvertLevel(x, side, pair))
                .ToArray();
        }

        private OrderBookLevel ConvertLevel(BookLevel x, CryptoOrderSide side, string? pair)
        {
            return new OrderBookLevel
            (
                x.OrderId > 0 ?
                    x.OrderId.ToString(CultureInfo.InvariantCulture) :
                    x.Price.ToString(CultureInfo.InvariantCulture),
                side,
                x.Price,
                x.Amount,
                null,
                pair
            );
        }

        private OrderBookLevel[] ConvertSnapshot(OrderBookResponse response)
        {
            var bids = ConvertLevels(response.Data.Bids, response.Symbol, CryptoOrderSide.Bid);
            var asks = ConvertLevels(response.Data.Asks, response.Symbol, CryptoOrderSide.Ask);

            var all = bids
                .Concat(asks)
                .Where(x => x.Amount > 0)
                .ToArray();
            return all;
        }

        private OrderBookLevelBulk[] ConvertDiff(OrderBookDiffResponse response)
        {
            var result = new List<OrderBookLevelBulk>();
            var bids = ConvertLevels(response.Data.Bids, response.Symbol, CryptoOrderSide.Bid);
            var asks = ConvertLevels(response.Data.Asks, response.Symbol, CryptoOrderSide.Ask);

            var all = bids.Concat(asks).ToArray();
            var toDelete = all.Where(x => x.Amount <= 0).ToArray();
            var toUpdate = all.Where(x => x.Amount > 0).ToArray();

            if (toDelete.Length > 0)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, toDelete, CryptoOrderBookType.L2)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = response.Data.Microtimestamp
                };
                result.Add(bulk);
            }

            if (toUpdate.Length > 0)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Update, toUpdate, CryptoOrderBookType.L2)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = response.Data.Microtimestamp
                };
                result.Add(bulk);
            }

            return result.ToArray();
        }

        /// <inheritdoc />
        protected override async Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000)
        {
            var snapshot = await LoadSnapshotRaw(pair, count);
            if (snapshot == null)
                return null;

            var levels = ConvertSnapshot(snapshot);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2)
            {
                ExchangeName = ExchangeName,
                ServerTimestamp = snapshot.Data.Microtimestamp
            };
            return bulk;
        }

        private async Task<OrderBookResponse?> LoadSnapshotRaw(string? pair, int count)
        {
            var pairSafe = (pair ?? string.Empty).Trim().ToLowerInvariant();
            var countSafe = count > 1000 ? 1000 : count;
            var result = string.Empty;

            try
            {
                var url = $"v2/order_book/{pairSafe}";
                using var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                using var content = response.Content;
                var parsed = await content.ReadFromJsonAsync<BitstampOrderBookResponse>(BitstampJson.Web.BitstampOrderBookResponse);
                if (parsed == null)
                    return null;

                var book = new OrderBookResponse
                {
                    Symbol = pairSafe,
                    Data = new OrderBook
                    {
                        Timestamp = parsed.timestamp,
                        Microtimestamp = parsed.microtimestamp,
                        Asks = parsed.asks.Take(countSafe).Select(x => new BookLevel
                        {
                            Side = OrderBookSide.Ask,
                            Price = x[0],
                            Amount = x[1]
                        }).ToArray(),
                        Bids = parsed.bids.Take(countSafe).Select(x => new BookLevel
                        {
                            Side = OrderBookSide.Bid,
                            Price = x[0],
                            Amount = x[1]
                        }).ToArray()
                    }
                };

                return book;
            }
            catch (Exception e)
            {
                _client.Logger.LogDebug($"[ORDER BOOK {ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                                        $"Error: '{e.Message}'.  Content: '{result}'");
                return null;
            }
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            var result = new List<OrderBookLevelBulk>();
            foreach (var response in data)
            {
                var responseSafe = response as OrderBookDiffResponse;
                if (responseSafe == null)
                    continue;

                var bulks = ConvertDiff(responseSafe);

                if (bulks.Length <= 0)
                    continue;

                result.AddRange(bulks);
            }

            return result.ToArray();
        }
    }

    [JsonSerializable(typeof(BitstampOrderBookResponse))]
    internal partial class BitstampJson : JsonSerializerContext
    {
        private static BitstampJson? _web;

        public static BitstampJson Web => _web ??= new BitstampJson(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    // ReSharper disable InconsistentNaming
    // ReSharper disable ClassNeverInstantiated.Global
    // ReSharper disable IdentifierTypo
    internal sealed class BitstampOrderBookResponse
    {
        [JsonConverter(typeof(DateTimeStringSecondsConverter))]
        public DateTime timestamp { get; set; }

        [JsonConverter(typeof(DateTimeStringMicrosecondsConverter))]
        public DateTime microtimestamp { get; set; }

        public IReadOnlyList<double[]> bids { get; set; } = [];
        public IReadOnlyList<double[]> asks { get; set; } = [];
    }

    internal sealed class DateTimeStringSecondsConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.UnixEpoch.AddSeconds(double.Parse(reader.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture));

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(ToUnixTimeSeconds(value).ToString(CultureInfo.InvariantCulture));

        private static long ToUnixTimeSeconds(DateTime value)
        {
            var timespan = value.ToUniversalTime().Subtract(DateTime.UnixEpoch);
            return timespan.Ticks / TimeSpan.TicksPerSecond;
        }
    }

    internal sealed class DateTimeStringMicrosecondsConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var microseconds = decimal.Parse(reader.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture);
            var milliseconds = microseconds / 1000;

            return DateTime.UnixEpoch.AddMilliseconds((double)milliseconds);
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(ToUnixTimeMicroseconds(value).ToString(CultureInfo.InvariantCulture));

        private static long ToUnixTimeMicroseconds(DateTime value)
        {
            var timespan = value.ToUniversalTime().Subtract(DateTime.UnixEpoch);
            return timespan.Ticks / 10;
        }
    }
}
