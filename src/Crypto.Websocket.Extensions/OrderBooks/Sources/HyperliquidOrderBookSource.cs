using System;
using System.Globalization;
using System.Linq;
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
            var bids = data.Levels.Length > 0 ? ConvertLevels(data.Levels[0], CryptoOrderSide.Bid, data.Coin) : [];
            var asks = data.Levels.Length > 1 ? ConvertLevels(data.Levels[1], CryptoOrderSide.Ask, data.Coin) : [];
            return bids.Concat(asks).ToArray();
        }

        private static OrderBookLevel[] ConvertLevels(Level[] levels, CryptoOrderSide side, string pair)
        {
            return levels.Select(x => ConvertLevel(x, side, pair)).ToArray();
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
