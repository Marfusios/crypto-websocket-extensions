using System;
using System.Globalization;
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
                return Array.Empty<OrderBookLevel>();

            var result = new OrderBookLevel[books.Length];
            for (var index = 0; index < books.Length; index++)
                result[index] = ConvertLevel(books[index], pair, side);

            return result;
        }

        private static OrderBookLevel[] ConvertLevels(Binance.Client.Websocket.Responses.Books.OrderBookLevel[]? books,
            string? pair, CryptoOrderSide side)
        {
            if (books == null)
                return Array.Empty<OrderBookLevel>();

            var result = new OrderBookLevel[books.Length];
            for (var index = 0; index < books.Length; index++)
                result[index] = ConvertLevel(books[index], pair, side);

            return result;
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
            var pairSafe = (pair ?? string.Empty).Trim().ToUpperInvariant();
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
            if (levels == null)
                return null;

            var result = new OrderBookLevelAster[levels.Length];
            for (var index = 0; index < levels.Length; index++)
                result[index] = ConvertRawLevel(levels[index]);

            return result;
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
            var count = (response.Bids?.Length ?? 0) + (response.Asks?.Length ?? 0);
            if (count == 0)
                return Array.Empty<OrderBookLevel>();

            var result = new OrderBookLevel[count];
            var index = 0;
            AddPositiveLevels(response.Bids, response.Symbol, CryptoOrderSide.Bid, result, ref index);
            AddPositiveLevels(response.Asks, response.Symbol, CryptoOrderSide.Ask, result, ref index);
            return TrimLevels(result, index);
        }

        private static OrderBookLevel[] ConvertSnapshot(OrderBookPartialBinance response)
        {
            var count = (response.Bids?.Length ?? 0) + (response.Asks?.Length ?? 0);
            if (count == 0)
                return Array.Empty<OrderBookLevel>();

            var result = new OrderBookLevel[count];
            var index = 0;
            AddPositiveLevels(response.Bids, response.Symbol, CryptoOrderSide.Bid, result, ref index);
            AddPositiveLevels(response.Asks, response.Symbol, CryptoOrderSide.Ask, result, ref index);
            return TrimLevels(result, index);
        }

        private OrderBookLevelBulk[] ConvertDiff(OrderBookDiffResponse response)
        {
            var data = response.Data;
            var count = (data?.Bids?.Length ?? 0) + (data?.Asks?.Length ?? 0);
            if (count == 0)
                return Array.Empty<OrderBookLevelBulk>();

            var toDelete = new OrderBookLevel[count];
            var toUpdate = new OrderBookLevel[count];
            var deleteCount = 0;
            var updateCount = 0;
            AddDiffLevels(data?.Bids, data?.Symbol, CryptoOrderSide.Bid, toDelete, ref deleteCount, toUpdate, ref updateCount);
            AddDiffLevels(data?.Asks, data?.Symbol, CryptoOrderSide.Ask, toDelete, ref deleteCount, toUpdate, ref updateCount);

            var result = new OrderBookLevelBulk[2];
            var resultCount = 0;

            if (deleteCount > 0)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, TrimLevels(toDelete, deleteCount), CryptoOrderBookType.L2)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = data?.EventTime,
                    ServerSequence = data?.LastUpdateId
                };
                result[resultCount++] = bulk;
            }

            if (updateCount > 0)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Update, TrimLevels(toUpdate, updateCount), CryptoOrderBookType.L2)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = data?.EventTime,
                    ServerSequence = data?.LastUpdateId
                };
                result[resultCount++] = bulk;
            }

            return TrimBulks(result, resultCount);
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object data)
        {
            return data is OrderBookDiffResponse response
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
                var responseSafe = response as OrderBookDiffResponse;
                if (responseSafe == null)
                    continue;

                var bulks = ConvertDiff(responseSafe);

                if (bulks.Length == 0)
                    continue;

                for (var index = 0; index < bulks.Length; index++)
                    result[count++] = bulks[index];
            }

            return TrimBulks(result, count);
        }

        private static void AddPositiveLevels(Aster.Client.Websocket.Responses.Books.OrderBookLevel[]? books,
            string? pair, CryptoOrderSide side, OrderBookLevel[] result, ref int index)
        {
            if (books == null)
                return;

            foreach (var book in books)
            {
                if (book.Quantity <= 0)
                    continue;

                result[index++] = ConvertLevel(book, pair, side);
            }
        }

        private static void AddPositiveLevels(Binance.Client.Websocket.Responses.Books.OrderBookLevel[]? books,
            string? pair, CryptoOrderSide side, OrderBookLevel[] result, ref int index)
        {
            if (books == null)
                return;

            foreach (var book in books)
            {
                if (book.Quantity <= 0)
                    continue;

                result[index++] = ConvertLevel(book, pair, side);
            }
        }

        private static void AddDiffLevels(Aster.Client.Websocket.Responses.Books.OrderBookLevel[]? books,
            string? pair,
            CryptoOrderSide side,
            OrderBookLevel[] toDelete,
            ref int deleteCount,
            OrderBookLevel[] toUpdate,
            ref int updateCount)
        {
            if (books == null)
                return;

            foreach (var book in books)
            {
                var level = ConvertLevel(book, pair, side);
                if (level.Amount <= 0)
                    toDelete[deleteCount++] = level;
                else
                    toUpdate[updateCount++] = level;
            }
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

    }
}
