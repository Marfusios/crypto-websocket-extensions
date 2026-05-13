using System;
using System.Globalization;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Hyperliquid.Client.Websocket.Client;
using Hyperliquid.Client.Websocket.Responses.Books;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class HyperliquidOrderBookSource : OrderBookSourceBase
    {
        private HyperliquidWebsocketClient _client = null!;
        private IDisposable? _subscriptionSnapshot;


        /// <inheritdoc />
        public HyperliquidOrderBookSource(HyperliquidWebsocketClient client) : base(client.Logger)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "hyperliquid";

        /// <inheritdoc />
        public override bool DiffsSupported => false;

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(HyperliquidWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            Subscribe();
        }

        /// <inheritdoc />
        protected override Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000)
        {
            return Task.FromResult<OrderBookLevelBulk?>(null);
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            throw new NotImplementedException();
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.L2BookStream.Subscribe(HandleSnapshot);
        }

        private void HandleSnapshot(BookResponse books)
        {
            // received snapshot, convert and stream
            var levels = ConvertLevels(books);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2)
            {
                ExchangeName = ExchangeName
            };
            StreamSnapshot(bulk);
        }

        private static OrderBookLevel[] ConvertLevels(BookResponse data)
        {
            var bids = data.Levels.Length > 0 ? data.Levels[0] : Array.Empty<Level>();
            var asks = data.Levels.Length > 1 ? data.Levels[1] : Array.Empty<Level>();
            var result = new OrderBookLevel[bids.Length + asks.Length];
            var index = 0;

            for (var bidIndex = 0; bidIndex < bids.Length; bidIndex++)
                result[index++] = ConvertLevel(bids[bidIndex], CryptoOrderSide.Bid, data.Coin);

            for (var askIndex = 0; askIndex < asks.Length; askIndex++)
                result[index++] = ConvertLevel(asks[askIndex], CryptoOrderSide.Ask, data.Coin);

            return result;
        }

        private static OrderBookLevel[] ConvertLevels(Level[] levels, CryptoOrderSide side, string pair)
        {
            var result = new OrderBookLevel[levels.Length];
            for (var index = 0; index < levels.Length; index++)
                result[index] = ConvertLevel(levels[index], side, pair);

            return result;
        }

        private static OrderBookLevel ConvertLevel(Level x, CryptoOrderSide side, string pair)
        {
            return new OrderBookLevel
            (
                x.Price.ToString(CultureInfo.InvariantCulture),
                side,
                x.Price,
                x.Size,
                x.NumberOfOrders,
                pair
            );
        }
    }
}
