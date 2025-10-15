using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Aster.Client.Websocket.Client;
using Aster.Client.Websocket.Responses.Books;
using Aster.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OrderBookLevel = Crypto.Websocket.Extensions.Core.OrderBooks.Models.OrderBookLevel;
using OrderBookPartialBinance = Binance.Client.Websocket.Responses.Books.OrderBookPartial;
using OrderBookLevelBinance = Binance.Client.Websocket.Responses.Books.OrderBookLevel;
using OrderBookLevelAster = Aster.Client.Websocket.Responses.Books.OrderBookLevel;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /*
     ## How to manage a local order book correctly
        1. Open a stream to wss://fstream.asterdex.com/stream?streams=btcusdt@depth.
        2. Buffer the events you receive from the stream. For same price, latest received update covers the previous one.
        3. Get a depth snapshot from https://fapi.asterdex.com/fapi/v1/depth?symbol=BTCUSDT&limit=1000 .
        4. Drop any event where u is < lastUpdateId in the snapshot.
        5. The first processed event should have U <= lastUpdateId AND u >= lastUpdateId
        6. While listening to the stream, each new event's pu should be equal to the previous event's u, otherwise initialize the process from step 3.
        7. The data in each event is the absolute quantity for a price level.
        8. If the quantity is 0, remove the price level.
        9. Receiving an event that removes a price level that is not in your local order book can happen and is normal.
     */

    /// <inheritdoc />
    public class AsterOrderBookSource : OrderBookSourceBase
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private AsterWebsocketClient _client = null!;
        private IDisposable? _subscriptionSnapshot;
        private IDisposable? _subscription;

        /// <inheritdoc />
        public AsterOrderBookSource(AsterWebsocketClient client) : base(client.Logger)
        {
            _httpClient.BaseAddress = new Uri("https://fapi.asterdex.com");

            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "aster";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(AsterWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            _subscription?.Dispose();
            Subscribe();
        }

        /// <summary>
        /// Request a new order book snapshot, will be fakely streamed via communicator (WebsocketClient)
        /// Method doesn't throw exception, just logs it
        /// </summary>
        /// <param name="communicator">Target communicator</param>
        /// <param name="pair">Target pair</param>
        /// <param name="count">Max level count</param>
        public async Task LoadSnapshot(AsterWebsocketCommunicator communicator, string pair, int count = 1000)
        {
            CryptoValidations.ValidateInput(communicator, nameof(communicator));

            var snapshot = await LoadSnapshotRaw(pair, count);
            if (snapshot == null)
                return;

            OrderBookPartialResponse.StreamFakeSnapshot(ConvertRawSnapshot(snapshot), communicator);
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.OrderBookPartialStream.Subscribe(HandleSnapshot);
            _subscription = _client.Streams.OrderBookDiffStream.Subscribe(HandleDiff);
        }

        private void HandleSnapshot(OrderBookPartialResponse response)
        {
            // received snapshot, convert and stream
            if (response.Data == null)
                return;

            var levels = ConvertSnapshot(response.Data);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2)
            {
                ExchangeName = ExchangeName,
                ServerSequence = response.Data.LastUpdateId
            };
            StreamSnapshot(bulk);
        }

        private void HandleDiff(OrderBookDiffResponse response)
        {
            BufferData(response);
        }

        private static OrderBookLevel[] ConvertLevels(Aster.Client.Websocket.Responses.Books.OrderBookLevel[]? books,
            string? pair, CryptoOrderSide side)
        {
            if (books == null)
                return [];

            return books
                .Select(x => ConvertLevel(x, pair, side))
                .ToArray();
        }

        private static OrderBookLevel[] ConvertLevels(Binance.Client.Websocket.Responses.Books.OrderBookLevel[]? books,
            string? pair, CryptoOrderSide side)
        {
            if (books == null)
                return [];

            return books
                .Select(x => ConvertLevel(x, pair, side))
                .ToArray();
        }

        private static OrderBookLevel ConvertLevel(Aster.Client.Websocket.Responses.Books.OrderBookLevel x, string? pair, CryptoOrderSide side)
        {
            return new OrderBookLevel
            (
                x.Price.ToString(CultureInfo.InvariantCulture),
                side,
                x.Price,
                x.Quantity,
                null,
                pair
            );
        }

        private static OrderBookLevel ConvertLevel(Binance.Client.Websocket.Responses.Books.OrderBookLevel x, string? pair, CryptoOrderSide side)
        {
            return new OrderBookLevel
            (
                x.Price.ToString(CultureInfo.InvariantCulture),
                side,
                x.Price,
                x.Quantity,
                null,
                pair
            );
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
                ServerSequence = snapshot.LastUpdateId
            };
            return bulk;
        }

        private async Task<OrderBookPartialBinance?> LoadSnapshotRaw(string? pair, int count)
        {
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            var countSafe = count > 1000 ? 1000 : count;
            var result = string.Empty;

            try
            {
                var url = $"/fapi/v1/depth?symbol={pairSafe}&limit={countSafe}";
                using var response = await _httpClient.GetAsync(url);
                using var content = response.Content;

                result = await content.ReadAsStringAsync();
                var parsed = JsonConvert.DeserializeObject<OrderBookPartialBinance>(result);
                if (parsed == null)
                    return null;

                parsed.Symbol = pairSafe;
                return parsed;
            }
            catch (Exception e)
            {
                _client.Logger.LogDebug($"[ORDER BOOK {ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                          $"Error: '{e.Message}'.  Content: '{result}'");
                return null;
            }
        }

        private OrderBookPartial ConvertRawSnapshot(OrderBookPartialBinance snapshot)
        {
            return new OrderBookPartial
            {
                LastUpdateId = snapshot.LastUpdateId,
                Bids = ConvertRawLevels(snapshot.Bids),
                Asks = ConvertRawLevels(snapshot.Asks),
                Symbol = snapshot.Symbol,
            };
        }

        private static OrderBookLevelAster[]? ConvertRawLevels(OrderBookLevelBinance[]? levels)
        {
            return levels?.Select(ConvertRawLevel)
                .ToArray();
        }

        private static OrderBookLevelAster ConvertRawLevel(OrderBookLevelBinance level)
        {
            return new OrderBookLevelAster
            {
                Price = level.Price,
                Quantity = level.Quantity
            };
        }

        private static OrderBookLevel[] ConvertSnapshot(OrderBookPartial response)
        {
            var bids = ConvertLevels(response.Bids, response.Symbol, CryptoOrderSide.Bid);
            var asks = ConvertLevels(response.Asks, response.Symbol, CryptoOrderSide.Ask);

            var all = bids
                .Concat(asks)
                .Where(x => x.Amount > 0)
                .ToArray();
            return all;
        }

        private static OrderBookLevel[] ConvertSnapshot(OrderBookPartialBinance response)
        {
            var bids = ConvertLevels(response.Bids, response.Symbol, CryptoOrderSide.Bid);
            var asks = ConvertLevels(response.Asks, response.Symbol, CryptoOrderSide.Ask);

            var all = bids
                .Concat(asks)
                .Where(x => x.Amount > 0)
                .ToArray();
            return all;
        }

        private OrderBookLevelBulk[] ConvertDiff(OrderBookDiffResponse response)
        {
            var result = new List<OrderBookLevelBulk>();
            var bids = ConvertLevels(response.Data?.Bids, response.Data?.Symbol, CryptoOrderSide.Bid);
            var asks = ConvertLevels(response.Data?.Asks, response.Data?.Symbol, CryptoOrderSide.Ask);

            var all = bids.Concat(asks).ToArray();
            var toDelete = all.Where(x => x.Amount <= 0).ToArray();
            var toUpdate = all.Where(x => x.Amount > 0).ToArray();

            if (toDelete.Any())
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, toDelete, CryptoOrderBookType.L2)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = response.Data?.EventTime,
                    ServerSequence = response.Data?.LastUpdateId
                };
                result.Add(bulk);
            }

            if (toUpdate.Any())
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Update, toUpdate, CryptoOrderBookType.L2)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = response.Data?.EventTime,
                    ServerSequence = response.Data?.LastUpdateId
                };
                result.Add(bulk);
            }

            return result.ToArray();
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

                if (!bulks.Any())
                    continue;

                result.AddRange(bulks);
            }

            return result.ToArray();
        }


    }
}
