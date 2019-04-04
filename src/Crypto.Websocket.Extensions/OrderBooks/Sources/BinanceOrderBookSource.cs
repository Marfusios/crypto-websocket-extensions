using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Binance.Client.Websocket.Client;
using Binance.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Logging;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.Validations;
using Newtonsoft.Json;
using OrderBookLevel = Crypto.Websocket.Extensions.OrderBooks.Models.OrderBookLevel;

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
    public class BinanceOrderBookSource : OrderBookLevel2SourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly BinanceWebsocketClient _client;
        private readonly HttpClient _httpClient = new HttpClient();

        /// <inheritdoc />
        public BinanceOrderBookSource(BinanceWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _httpClient.BaseAddress = new Uri("https://www.binance.com");

            Subscribe();
        }

        /// <inheritdoc />
        public override string ExchangeName => "binance";

        /// <summary>
        /// Load snapshot via HTTP (REST call).
        /// Need to be called before anything else. 
        /// </summary>
        public async Task LoadSnapshot(string pair, int count = 1000)
        {
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            var url = $"/api/v1/depth?symbol={pairSafe}&limit={count}";
            using (HttpResponseMessage response = await _httpClient.GetAsync(url))
            using (HttpContent content = response.Content)
            {
                OrderBookPartial parsed = null;
                try
                {
                    var result = await content.ReadAsStringAsync();
                    parsed = JsonConvert.DeserializeObject<OrderBookPartial>(result);
                    if (parsed == null)
                        return;

                    parsed.Symbol = pairSafe;
                }
                catch (Exception e)
                {
                    Log.Warn($"Failed to load orderbook snapshot for pair '{pairSafe}'. Error: {e.Message}");
                    return;
                }
               
                HandleSnapshot(parsed);
            }
        }

        private void Subscribe()
        {
            _client.Streams.OrderBookDiffStream.Subscribe(HandleDiff);
        }

        private void HandleSnapshot(OrderBookPartial response)
        {
            var bids = ConvertLevels(response.Bids, response.Symbol, CryptoSide.Bid);
            var asks = ConvertLevels(response.Asks, response.Symbol, CryptoSide.Ask);

            var all = bids
                .Concat(asks)
                .Where(x => x.Amount > 0)
                .ToArray();
            OrderBookSnapshotSubject.OnNext(all);
        }

        private void HandleDiff(OrderBookDiffResponse response)
        {
            var bids = ConvertLevels(response.Data?.Bids, response.Data?.Symbol, CryptoSide.Bid);
            var asks = ConvertLevels(response.Data?.Asks, response.Data?.Symbol, CryptoSide.Ask);
            
            var all = bids.Concat(asks).ToArray();
            var toDelete = all.Where(x => x.Amount <= 0).ToArray();
            var toUpdate = all.Where(x => x.Amount > 0).ToArray();

            if (toDelete.Any())
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, toDelete);
                OrderBookSubject.OnNext(bulk);
            }

            if (toUpdate.Any())
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Update, toUpdate);
                OrderBookSubject.OnNext(bulk);
            }
        }

        private OrderBookLevel[] ConvertLevels(Binance.Client.Websocket.Responses.Books.OrderBookLevel[] books, 
            string pair, CryptoSide side)
        {
            if (books == null)
                return new OrderBookLevel[0];

            return books
                .Select(x => ConvertLevel(x , pair, side))
                .ToArray();
        }

        private OrderBookLevel ConvertLevel(Binance.Client.Websocket.Responses.Books.OrderBookLevel x, 
            string pair, CryptoSide side)
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
    }
}
