using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Responses;
using Bitmex.Client.Websocket.Responses.Books;
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
    /// <inheritdoc />
    public class BitmexOrderBookSource : OrderBookSourceBase
    {
        static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        readonly HttpClient _httpClient = new();
        BitmexWebsocketClient _client;
        IDisposable _subscription;

        /// <inheritdoc />
        public BitmexOrderBookSource(BitmexWebsocketClient client, bool isTestnet = false)
        {
            _httpClient.BaseAddress = isTestnet ?
                new Uri("https://testnet.bitmex.com") :
                new Uri("https://www.bitmex.com");

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

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();
            _subscription?.Dispose();
        }

        void Subscribe()
        {
            _subscription = _client.Streams.BookStream.Subscribe(HandleBookResponse);
        }

        void HandleBookResponse(BookResponse bookResponse)
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
                var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2)
                {
                    ExchangeName = ExchangeName
                };
                StreamSnapshot(bulk);
                return;
            }

            // received difference, buffer it
            BufferData(bookResponse);
        }

        static OrderBookLevel[] ConvertLevels(BookLevel[] data)
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

        static CryptoOrderSide ConvertSide(BitmexSide side)
        {
            return side switch
            {
                BitmexSide.Buy => CryptoOrderSide.Bid,
                BitmexSide.Sell => CryptoOrderSide.Ask,
                _ => CryptoOrderSide.Undefined
            };
        }

        static OrderBookAction ConvertAction(BitmexAction action)
        {
            return action switch
            {
                BitmexAction.Insert => OrderBookAction.Insert,
                BitmexAction.Update => OrderBookAction.Update,
                BitmexAction.Delete => OrderBookAction.Delete,
                _ => OrderBookAction.Undefined
            };
        }

        /// <inheritdoc />
        protected override async Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count)
        {
            BookLevel[] parsed = null;
            var pairSafe = (pair ?? string.Empty).Trim().ToUpper();
            var countSafe = count > 1000 ? 0 : count;
            var result = string.Empty;

            try
            {
                var url = $"/api/v1/orderBook/L2?symbol={pairSafe}&depth={countSafe}";
                using var response = await _httpClient.GetAsync(url);
                using var content = response.Content;
                result = await content.ReadAsStringAsync();
                parsed = JsonConvert.DeserializeObject<BookLevel[]>(result);

                if (parsed == null || !parsed.Any())
                    return null;
            }
            catch (Exception e)
            {
                Log.Debug($"[ORDER BOOK {ExchangeName}] Failed to load orderbook snapshot for pair '{pairSafe}'. " +
                         $"Error: '{e.Message}'.  Content: '{result}'");
                return null;
            }

            // received snapshot, convert and stream
            var levels = ConvertLevels(parsed);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2)
            {
                ExchangeName = ExchangeName
            };
            return bulk;
        }

        OrderBookLevelBulk ConvertDiff(BookResponse response)
        {
            var action = ConvertAction(response.Action);
            var bulk = new OrderBookLevelBulk(action, ConvertLevels(response.Data), CryptoOrderBookType.L2)
            {
                ExchangeName = ExchangeName
            };
            return bulk;
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            var result = new List<OrderBookLevelBulk>();
            foreach (var response in data)
            {
                if (response is BookResponse responseSafe)
                {
                    var converted = ConvertDiff(responseSafe);
                    result.Add(converted);
                }
            }

            return result.ToArray();
        }
    }
}
