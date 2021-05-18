using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;
using Huobi.Client.Websocket.Clients;
using Huobi.Client.Websocket.Messages.MarketData;
using Huobi.Client.Websocket.Messages.MarketData.MarketByPrice;
using Huobi.Client.Websocket.Messages.MarketData.Values;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <summary>
    /// Bitstamp order book source - based only on 100 level snapshots (not diffs)
    /// </summary>
    public class HuobiOrderBookSource : OrderBookSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private IHuobiMarketByPriceWebsocketClient _client;
        private IDisposable _subscription;
        private IDisposable _subscriptionSnapshot;

        /// <inheritdoc />
        public HuobiOrderBookSource(IHuobiMarketByPriceWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "huobi";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(IHuobiMarketByPriceWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            _subscription?.Dispose();
            Subscribe();
        }

        protected override OrderBookLevelBulk[] ConvertData(object[] data)
        {
            var result = new List<OrderBookLevelBulk>();

            foreach (var item in data)
            {
                if (item is MarketByPriceUpdateMessage message)
                {
                    var levels = ConvertLevels(message);
                    var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
                    FillBulk(message, bulk);
                    result.Add(bulk);
                }
            }

            return result.ToArray();
        }

        protected override Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count)
        {
            // TODO: how to simulate and what to do with count?

            // request a snapshot which will be received by Handle method
            var request = new MarketByPricePullRequest("pX", pair, 20);
            _client.Send(request);

            // nothing to stream here...
            return Task.FromResult(default(OrderBookLevelBulk));
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.MarketByPriceUpdateStream.Subscribe(HandleUpdate);
            _subscriptionSnapshot = _client.Streams.MarketByPricePullStream.Subscribe(HandleSnapshot);
        }

        private void HandleUpdate(MarketByPriceUpdateMessage response)
        {
            // TODO: how to force refresh when sequence number does not match?
            BufferData(response);
        }

        private void HandleSnapshot(MarketByPricePullResponse response)
        {
            // received snapshot, convert and stream
            var levels = ConvertLevels(response);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
            FillBulk(response, bulk);
            StreamSnapshot(bulk);
        }

        private OrderBookLevel[] ConvertLevels(MarketByPriceUpdateMessage response)
        {
            var symbol = response.ParseSymbolFromTopic();
            return ConvertLevels(symbol, response.Tick?.Bids, response.Tick?.Asks);
        }

        private OrderBookLevel[] ConvertLevels(MarketByPricePullResponse response)
        {
            var symbol = response.ParseSymbolFromTopic();
            return ConvertLevels(symbol, response.Data?.Bids, response.Data?.Asks);
        }

        private OrderBookLevel[] ConvertLevels(string symbol, BookLevel[] bids, BookLevel[] asks)
        {
            var convertedBids = bids?.Select(x => ConvertLevel(x, CryptoOrderSide.Bid, symbol)).ToArray()
                             ?? Array.Empty<OrderBookLevel>();
            var convertedAsks = asks?.Select(x => ConvertLevel(x, CryptoOrderSide.Ask, symbol)).ToArray()
                             ?? Array.Empty<OrderBookLevel>();
            return convertedBids.Concat(convertedAsks).ToArray();
        }

        private OrderBookLevel ConvertLevel(BookLevel x, CryptoOrderSide side, string pair)
        {
            return new OrderBookLevel(
                x.Price.ToString(CultureInfo.InvariantCulture),
                side,
                (double)x.Price,
                (double)x.Size,
                null, // TODO: really null?
                pair);
        }

        private void FillBulk(MarketByPriceUpdateMessage message, OrderBookLevelBulk bulk)
        {
            if (message == null)
            {
                return;
            }

            bulk.ExchangeName = ExchangeName;
            bulk.ServerTimestamp = message.Timestamp.DateTime;
        }

        private void FillBulk(MarketByPricePullResponse response, OrderBookLevelBulk bulk)
        {
            if (response == null)
            {
                return;
            }

            bulk.ExchangeName = ExchangeName;
            bulk.ServerTimestamp = response.Timestamp.DateTime;
        }
    }
}