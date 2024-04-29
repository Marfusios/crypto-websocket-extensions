﻿using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Bitstamp.Client.Websocket.Client;
using Bitstamp.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <summary>
    /// Bitstamp order book source - based only on 100 level snapshots (not diffs) 
    /// </summary>
    public class BitstampOrderBookSource : OrderBookSourceBase
    {
        private BitstampWebsocketClient _client = null!;
        private IDisposable? _subscriptionSnapshot;

        /// <inheritdoc />
        public BitstampOrderBookSource(BitstampWebsocketClient client) : base(client.Logger)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitstamp";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitstampWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.OrderBookStream.Subscribe(HandleSnapshot);
        }

        private void HandleSnapshot(OrderBookResponse response)
        {
            // received snapshot, convert and stream
            var levels = ConvertLevels(response);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
            FillBulk(response, bulk);
            StreamSnapshot(bulk);
        }



        private OrderBookLevel[] ConvertLevels(OrderBookResponse response)
        {
            var bids = response.Data?.Bids
                .Select(x => ConvertLevel(x, CryptoOrderSide.Bid, response.Symbol))
                .ToArray() ?? Array.Empty<OrderBookLevel>();
            var asks = response.Data?.Asks
                .Select(x => ConvertLevel(x, CryptoOrderSide.Ask, response.Symbol))
                .ToArray() ?? Array.Empty<OrderBookLevel>();
            return bids.Concat(asks).ToArray();
        }

        private OrderBookLevel ConvertLevel(BookLevel x, CryptoOrderSide side, string pair)
        {
            return new OrderBookLevel
            (
                x.OrderId > 0 ?
                    x.OrderId.ToString(CultureInfo.InvariantCulture) :
                    x.Price.ToString(CultureInfo.InvariantCulture),
                side,
                x.Price,
                x.Amount,
                null,
                pair
            );
        }

        /// <inheritdoc />
        protected override Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000)
        {
            return Task.FromResult<OrderBookLevelBulk?>(null);
        }

        private void FillBulk(OrderBookResponse? response, OrderBookLevelBulk bulk)
        {
            if (response == null)
                return;

            bulk.ExchangeName = ExchangeName;
            bulk.ServerTimestamp = response.Data?.Microtimestamp;
        }

        /// <inheritdoc />
        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            return Array.Empty<OrderBookLevelBulk>();
        }
    }
}
