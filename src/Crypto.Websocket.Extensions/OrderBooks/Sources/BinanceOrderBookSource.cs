using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Binance.Client.Websocket.Client;
using Binance.Client.Websocket.Responses.Books;
using Binance.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;
using Newtonsoft.Json;
using OrderBookLevel = Crypto.Websocket.Extensions.Core.OrderBooks.Models.OrderBookLevel;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /*
     ## How to manage a local order book correctly
        1. Open a stream to **wss://stream.binance.com:9443/ws/bnbbtc@depth**
        2. Buffer the events you receive from the stream
        3. Get a depth snapshot from **https://www.binance.com/api/v1/depth?symbol=BNBBTC&limit=1000**
        4. Drop any event where `u` is <= `lastUpdateId` in the snapshot
        5. The first processed should have `U` <= `lastUpdateId`+1 **AND** `u` >= `lastUpdateId`+1
        6. While listening to the stream, each new event's `U` should be equal to the previous event's `u`+1
        7. The data in each event is the **absolute** quantity for a price level
        8. If the quantity is 0, **remove** the price level
        9. Receiving an event that removes a price level that is not in your local order book can happen and is normal.
     */


    /// <inheritdoc />
    public class BinanceOrderBookSource : OrderBookSourceBase
    {
        static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        readonly HttpClient _httpClient = new();
        BinanceWebsocketClient _client;
        IDisposable _snapshotSubscription;
        IDisposable _diffSubscription;

        /// <inheritdoc />
        public BinanceOrderBookSource(BinanceWebsocketClient client)
        {
            _httpClient.BaseAddress = new Uri("https://www.binance.com");

            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "binance";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BinanceWebsocketClient client)
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

        /// <summary>
        /// Request a new order book snapshot, will be fakely streamed via communicator (WebsocketClient)
        /// Method doesn't throw exception, just logs it
        /// </summary>
        /// <param name="communicator">Target communicator</param>
        /// <param name="pair">Target pair</param>
        /// <param name="count">Max level count</param>
        public async Task LoadSnapshot(BinanceWebsocketCommunicator communicator, string pair, int count = 1000)
        {
            CryptoValidations.ValidateInput(communicator, nameof(communicator));

            var snapshot = await LoadSnapshotRaw(pair, count);
            if (snapshot == null)
                return;

            OrderBookPartialResponse.StreamFakeSnapshot(snapshot, communicator);
        }

        void Subscribe()
        {
            _snapshotSubscription = _client.Streams.OrderBookPartialStream.Subscribe(HandleSnapshot);
            _diffSubscription = _client.Streams.OrderBookDiffStream.Subscribe(HandleDiff);
        }

        void HandleSnapshot(OrderBookPartialResponse response)
        {
            // received snapshot, convert and stream
            var levels = ConvertSnapshot(response.Data);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2)
            {
                ExchangeName = ExchangeName,
                ServerSequence = response.Data.LastUpdateId
            };
            StreamSnapshot(bulk);
        }

        void HandleDiff(OrderBookDiffResponse response)
        {
            BufferData(response);
        }

        static OrderBookLevel[] ConvertLevels(Binance.Client.Websocket.Responses.Books.OrderBookLevel[] books, string pair, CryptoOrderSide side)
        {
            if (books == null)
                return Array.Empty<OrderBookLevel>();

            return books
                .Select(x => ConvertLevel(x , pair, side))
                .ToArray();
        }

        static OrderBookLevel ConvertLevel(Binance.Client.Websocket.Responses.Books.OrderBookLevel x, string pair, CryptoOrderSide side)
        {
            return new
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
        protected override async Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count)
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

        async Task<OrderBookPartial> LoadSnapshotRaw(string pair, int count)
        {
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            var countSafe = count > 1000 ? 1000 : count;
            var result = string.Empty;

            try
            {
                var url = $"/api/v1/depth?symbol={pairSafe}&limit={countSafe}";
                using var response = await _httpClient.GetAsync(url);
                using var content = response.Content;
                result = await content.ReadAsStringAsync();
                var parsed = JsonConvert.DeserializeObject<OrderBookPartial>(result);
                
                if (parsed == null)
                    return null;

                parsed.Symbol = pairSafe;
                return parsed;
            }
            catch (Exception e)
            {
                Log.Debug($"[ORDER BOOK {ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                          $"Error: '{e.Message}'.  Content: '{result}'");
                return null;
            }
        }

        static OrderBookLevel[] ConvertSnapshot(OrderBookPartial response)
        {
            var bids = ConvertLevels(response.Bids, response.Symbol, CryptoOrderSide.Bid);
            var asks = ConvertLevels(response.Asks, response.Symbol, CryptoOrderSide.Ask);

            var all = bids
                .Concat(asks)
                .Where(x => x.Amount > 0)
                .ToArray();
            return all;
        }

        OrderBookLevelBulk[] ConvertDiff(OrderBookDiffResponse response)
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
                if (response is OrderBookDiffResponse responseSafe)
                {
                    var bulks = ConvertDiff(responseSafe);

                    if(!bulks.Any())
                        continue;

                    result.AddRange(bulks);
                }
            }

            return result.ToArray();
        }

        
    }
}
