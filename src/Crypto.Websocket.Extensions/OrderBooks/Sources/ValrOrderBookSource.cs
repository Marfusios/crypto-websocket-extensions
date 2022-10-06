using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Valr.Client.Websocket.Client;
using Valr.Client.Websocket.Responses;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <summary>
    /// Valr order book source - based only on 100 level snapshots (not diffs)
    /// </summary>
    public class ValrOrderBookSource : OrderBookSourceBase
    {
        IValrTradeWebsocketClient _client;
        IDisposable _snapshotSubscription;

        /// <inheritdoc />
        public ValrOrderBookSource(IValrTradeWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "valr";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(IValrTradeWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _snapshotSubscription?.Dispose();
            Subscribe();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();
            _snapshotSubscription?.Dispose();
        }

        void Subscribe()
        {
            _snapshotSubscription = _client.Streams.AggregatedOrderBookUpdateStream.Subscribe(HandleSnapshot);
        }

        void HandleSnapshot(AggregatedOrderBookUpdateResponse response)
        {
            // received snapshot, convert and stream
            var levels = ConvertLevels(response);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
            FillBulk(response, bulk);
            StreamSnapshot(bulk);
        }

        static OrderBookLevel[] ConvertLevels(AggregatedOrderBookUpdateResponse response)
        {
            var bids = response.Data.Bids
                .Select(x => ConvertLevel(x, CryptoOrderSide.Bid, response.CurrencyPairSymbol))
                .ToArray();
            var asks = response.Data.Asks
                .Select(x => ConvertLevel(x, CryptoOrderSide.Ask, response.CurrencyPairSymbol))
                .ToArray();
            return bids.Concat(asks).ToArray();
        }

        static OrderBookLevel ConvertLevel(Quote x, CryptoOrderSide side, string pair)
        {
            return new
            (
                x.Price.ToString(CultureInfo.InvariantCulture),
                side,
                x.Price,
                x.Quantity,
                x.OrderCount,
                pair
            );
        }

        /// <inheritdoc />
        protected override Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count = 1000)
        {
            return Task.FromResult<OrderBookLevelBulk>(null);
        }

        void FillBulk(AggregatedOrderBookUpdateResponse response, OrderBookLevelBulk bulk)
        {
            if (response == null)
                return;

            bulk.ExchangeName = ExchangeName;
            bulk.ServerTimestamp = response.Data.LastChange;
            bulk.ServerSequence = response.Data.SequenceNumber;
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            return Array.Empty<OrderBookLevelBulk>();
        }
    }
}
