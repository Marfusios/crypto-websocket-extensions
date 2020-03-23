using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bitstamp.Client.Websocket.Channels;
using Bitstamp.Client.Websocket.Client;
using Bitstamp.Client.Websocket.Requests;
using Bitstamp.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;
using Newtonsoft.Json;
using OrderBookLevel = Crypto.Websocket.Extensions.Core.OrderBooks.Models.OrderBookLevel;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    public class BitstampOrderBookSource : OrderBookLevel2SourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly HttpClient _httpClient = new HttpClient();
        private BitstampWebsocketClient _client;
        private IDisposable _subscription;
        private IDisposable _subscriptionSnapshot;


        /// <inheritdoc />
        public BitstampOrderBookSource(BitstampWebsocketClient client)
        {
            _httpClient.BaseAddress = new Uri("https://www.bitstamp.net");

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
            _subscriptionSnapshot?.Dispose();
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.OrderBookDiffStream.Subscribe(x => HandleSnapshot(x));
            //_subscriptionSnapshot = _client.Streams.OrderBookSnapshotStream.Subscribe(HandleSnapshot);
            _subscriptionSnapshot = _client.Streams.OrderBookDetailStream.Subscribe(HandleSnapshot);

            //_client.Send(new UnsubscribeRequest(pair, Channel.OrderBookDetail));

            //_client.Send(new UnsubscribeRequest(pair, Channel.OrderBookDetail));

            //_subscription = _client.Streams.OrderBookDiffStream.Subscribe(HandleBook);
            //TODO
            //_subscriptionFull = _client.Streams.OrderBookDiffStream.Subscribe(HandleSnapshot);
        }

        private void HandleSnapshot(OrderBookDiffResponse snapshot)
        {
            // received snapshot, convert and stream
            var levels = ConvertSnapshot(snapshot);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels);
            FillBulk(bulk);
            StreamSnapshot(bulk);
        }

        private void HandleSnapshot(OrderBookDetailResponse snapshot)
        {
            // received snapshot, convert and stream
            var levels = ConvertSnapshot(snapshot);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels);
            FillBulk(bulk);
            StreamSnapshot(bulk);
        }


        private void HandleSnapshot(OrderBookSnapshotResponse snapshot)
        {
            // received snapshot, convert and stream
            var levels = ConvertSnapshot(snapshot);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels);
            FillBulk(bulk);
            StreamSnapshot(bulk);
        }

        /// <inheritdoc />
        protected override async Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count)
        {
            var snapshot = await LoadSnapshotRaw(pair, count);
            if (snapshot == null) return null;

            var levels = ConvertSnapshot(snapshot);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels)
            {
                ExchangeName = ExchangeName,
                ServerSequence = snapshot.Timestamp
            };
            return bulk;
        }

        private async Task<OrderBookSnapshotResponse> LoadSnapshotRaw(string pair, int count = 0)
        {
            var pairSafe = (pair ?? string.Empty).Trim().ToLower();
            var result = string.Empty;

            try
            {
                var url = $"/api/v2/order_book/{pairSafe}";
                using (var response = await _httpClient.GetAsync(url))
                using (var content = response.Content)
                {
                    result = await content.ReadAsStringAsync();
                    var parsed = JsonConvert.DeserializeObject<OrderBookSnapshotResponse>(result);
                    if (parsed == null) return null;

                    parsed.Symbol = pairSafe;
                    return parsed;
                }
            }
            catch (Exception e)
            {
                Log.Debug($"[ORDER BOOK {ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                          $"Error: '{e.Message}'.  Content: '{result}'");
                return null;
            }
        }

        private OrderBookLevel[] ConvertSnapshot(OrderBookDetailResponse snapshot)
        {
            // need to get symbol from channel?
            var bids = ConvertLevels(snapshot.Symbol, snapshot.Data.Bids);
            var asks = ConvertLevels(snapshot.Symbol, snapshot.Data.Asks);
            var levels = bids.Concat(asks).ToArray();
            return levels;
        }

        private OrderBookLevel[] ConvertSnapshot(OrderBookDiffResponse response)
        {
            // need to get symbol from channel?
            var bids = ConvertLevels(response.Symbol, response.Data.Bids);
            var asks = ConvertLevels(response.Symbol, response.Data.Asks);
            var levels = bids.Concat(asks).ToArray();
            return levels;
        }


        private OrderBookLevel[] ConvertSnapshot(OrderBookSnapshotResponse snapshot)
        {
            // need to get symbol from channel?
            var bids = ConvertLevels(snapshot.Symbol, snapshot.Bids, CryptoOrderSide.Bid);
            var asks = ConvertLevels(snapshot.Symbol, snapshot.Asks, CryptoOrderSide.Ask);
            var levels = bids.Concat(asks).ToArray();
            return levels;
        }

        private void HandleBook(OrderBookDiffResponse update)
        {
            BufferData(update);
        }

        private OrderBookLevel[] ConvertLevels(string pair, BookLevel[] data)
        {
            return data
                .Select(x => ConvertLevel(pair, x))
                .ToArray();
        }

        private OrderBookLevel[] ConvertLevels(string pair, SnapshotBookLevel[] data, CryptoOrderSide side)
        {
            return data
                .Select(x => ConvertLevel(pair, x, side))
                .ToArray();
        }

        private OrderBookLevel ConvertLevel(string pair, BookLevel x)
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

        private OrderBookLevel ConvertLevel(string pair, SnapshotBookLevel x, CryptoOrderSide side)
        {
            return new OrderBookLevel
            (
                x.Price.ToString(CultureInfo.InvariantCulture),
                side,
                x.Price,
                x.Amount,
                null,
                pair
            );
        }

        private CryptoOrderSide ConvertSide(OrderBookSide side)
        {
            if (side == OrderBookSide.Buy) return CryptoOrderSide.Bid;

            if (side == OrderBookSide.Sell) return CryptoOrderSide.Ask;

            return CryptoOrderSide.Undefined;
        }

        private OrderBookAction RecognizeAction(OrderBookLevel level)
        {
            if (level.Amount > 0) return OrderBookAction.Update;

            return OrderBookAction.Delete;
        }

        private IEnumerable<OrderBookLevelBulk> ConvertDiff(OrderBookDiffResponse update)
        {
            var bids = ConvertLevels(update.Symbol, update.Data.Bids);
            var asks = ConvertLevels(update.Symbol, update.Data.Asks);
            var converted = bids.Concat(asks).ToArray();
            var group = converted.GroupBy(RecognizeAction).ToArray();

            foreach (var actionGroup in group)
            {
                var bulk = new OrderBookLevelBulk(actionGroup.Key, actionGroup.ToArray());
                yield return bulk;
            }
        }

        private void FillBulk(OrderBookLevelBulk bulk)
        {
            bulk.ExchangeName = ExchangeName;
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            var result = new List<OrderBookLevelBulk>();
            foreach (var response in data)
            {
                var responseSafe = response as OrderBookDiffResponse;
                if (responseSafe == null) continue;

                var converted = ConvertDiff(responseSafe);
                result.AddRange(converted);
            }

            return result.ToArray();
        }
    }
}