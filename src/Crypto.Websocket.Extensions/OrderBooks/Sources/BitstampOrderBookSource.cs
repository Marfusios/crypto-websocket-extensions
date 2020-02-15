using Bitstamp.Client.Websocket.Client;
using Bitstamp.Client.Websocket.Responses;
using Bitstamp.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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
        private IDisposable _subscriptionFull;


        /// <inheritdoc />
        public BitstampOrderBookSource(BitstampWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitstamp";

        /// <summary>
        ///     Change client and resubscribe to the new streams
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
            _subscriptionSnapshot = _client.Streams.OrderBookDetailStream.Subscribe(HandleSnapshot);
            _subscription = _client.Streams.OrderBookFullStream.Subscribe(HandleBook);
            //TODO
            //_subscriptionFull = _client.Streams.OrderBookFullStream.Subscribe(HandleSnapshot);
        }

        private void HandleSnapshot(OrderBookDetailResponse snapshot)
        {
            // received snapshot, convert and stream
            var levels = ConvertSnapshot(snapshot);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels);
            FillBulk(bulk);
            StreamSnapshot(bulk);

            // received snapshot, convert and stream
            //var levels = ConvertSnapshot(snapshot);
            //StreamSnapshot(levels);
        }

        private OrderBookLevel[] ConvertSnapshot(OrderBookDetailResponse snapshot)
        {
            // need to get symbol from channel?
            var bids = ConvertLevels(snapshot.Symbol, snapshot.Bids);
            var asks = ConvertLevels(snapshot.Symbol, snapshot.Asks);
            var levels = bids.Concat(asks).ToArray();
            return levels;
        }

        private void HandleBook(OrderBookFullResponse update)
        {
            BufferData(update);
        }

        private OrderBookLevel[] ConvertLevels(string pair,
            Bitstamp.Client.Websocket.Responses.Books.OrderBookLevel[] data)
        {
            return data
                .Select(x => ConvertLevel(pair, x))
                .ToArray();
        }

        private OrderBookLevel ConvertLevel(string pair, Bitstamp.Client.Websocket.Responses.Books.OrderBookLevel x)
        {
            return new OrderBookLevel
            (
                x.Price.ToString(),
                ConvertSide(x.Side),
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

        /// <inheritdoc />
        protected override async Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count)
        {
            /*
            OrderBookSnapshotDto parsed = null;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            var result = string.Empty;

            try
            {
                var url = $"/products/{pairSafe}/book?level=2";
                using (var response = await _httpClient.GetAsync(url))
                using (var content = response.Content)
                {
                    result = await content.ReadAsStringAsync();
                    parsed = JsonConvert.DeserializeObject<OrderBookSnapshotDto>(result);
                    if (parsed == null)
                        return null;
                }
            }
            catch (Exception e)
            {
                Log.Debug($"[ORDER BOOK {ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                          $"Error: '{e.Message}'.  Content: '{result}'");
                return null;
            }

            var bids = ConvertLevels(pair, parsed.Bids);
            var asks = ConvertLevels(pair, parsed.Asks);
            var levels = bids.Concat(asks).ToArray();
            */
            //return levels;
            throw new NotImplementedException();
        }

        private IEnumerable<OrderBookLevelBulk> ConvertDiff(OrderBookFullResponse update)
        {
            var bids = ConvertLevels(update.Symbol, update.Bids);
            var asks = ConvertLevels(update.Symbol, update.Asks);
            var converted = bids.Concat(asks).ToArray();

            // var converted = ConvertLevels(update.Symbol, update.Asks);

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
                var responseSafe = response as OrderBookFullResponse;
                if (responseSafe == null) continue;

                var converted = ConvertDiff(responseSafe);
                result.AddRange(converted);
            }

            return result.ToArray();
        }
    }
}