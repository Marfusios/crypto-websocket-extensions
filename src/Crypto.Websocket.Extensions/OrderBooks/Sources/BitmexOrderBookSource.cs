using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Responses;
using Bitmex.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;
using Newtonsoft.Json;
using OrderBookLevel = Crypto.Websocket.Extensions.Core.OrderBooks.Models.OrderBookLevel;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class BitmexOrderBookSource : OrderBookLevel2SourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly HttpClient _httpClient = new HttpClient();
        private BitmexWebsocketClient _client;
        private IDisposable _subscription;

        /// <inheritdoc />
        public BitmexOrderBookSource(BitmexWebsocketClient client)
        {
            _httpClient.BaseAddress = new Uri("https://www.bitmex.com");

            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitmex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitmexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.BookStream.Subscribe(HandleBookResponse);
        }

        private void HandleBookResponse(BookResponse bookResponse)
        {
            if (bookResponse.Action == BitmexAction.Undefined)
            {
                // weird state, do nothing
                return;
            }

            if (bookResponse.Action == BitmexAction.Partial)
            {
                // received snapshot, convert and stream
                var levels = ConvertLevels(bookResponse.Data);
                StreamSnapshot(levels);
                return;
            }

            // received difference, buffer it
            BufferData(bookResponse);
        }

        private OrderBookLevel[] ConvertLevels(BookLevel[] data)
        {
            return data
                .Select(x => new OrderBookLevel
                (
                    x.Id.ToString(),
                    ConvertSide(x.Side),
                    x.Price,
                    x.Size,
                    null,
                    x.Symbol
                ))
                .ToArray();
        }

        private CryptoOrderSide ConvertSide(BitmexSide side)
        {
            switch (side)
            {
                case BitmexSide.Buy:
                    return CryptoOrderSide.Bid;
                case BitmexSide.Sell:
                    return CryptoOrderSide.Ask;
                default:
                    return CryptoOrderSide.Undefined;
            }
        }

        private OrderBookAction ConvertAction(BitmexAction action)
        {
            switch (action)
            {
                case BitmexAction.Insert:
                    return OrderBookAction.Insert;
                case BitmexAction.Update:
                    return OrderBookAction.Update;
                case BitmexAction.Delete:
                    return OrderBookAction.Delete;
                default:
                    return OrderBookAction.Undefined;
            }
        }

        /// <inheritdoc />
        protected override async Task<OrderBookLevel[]> LoadSnapshotInternal(string pair, int count)
        {
            BookLevel[] parsed = null;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            var countSafe = count > 1000 ? 0 : count;

            try
            {
                var url = $"/api/v1/orderBook/L2?symbol={pairSafe}&depth={countSafe}";
                using (HttpResponseMessage response = await _httpClient.GetAsync(url))
                using (HttpContent content = response.Content)
                {
                    var result = await content.ReadAsStringAsync();
                    parsed = JsonConvert.DeserializeObject<BookLevel[]>(result);
                    if (parsed == null || !parsed.Any())
                        return null;
                }
            }
            catch (Exception e)
            {
                Log.Debug($"[{ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                         $"Error: {e.Message}");
                return null;
            }

            // received snapshot, convert and stream
            return ConvertLevels(parsed);
        }

        private OrderBookLevelBulk ConvertDiff(BookResponse response)
        {
            var action = ConvertAction(response.Action);
            var bulk = new OrderBookLevelBulk(action, ConvertLevels(response.Data));
            return bulk;
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            var result = new List<OrderBookLevelBulk>();
            foreach (var response in data)
            {
                var responseSafe = response as BookResponse;
                if(responseSafe == null)
                    continue;

                var converted = ConvertDiff(responseSafe);
                result.Add(converted);
            }

            return result.ToArray();
        }
    }
}
