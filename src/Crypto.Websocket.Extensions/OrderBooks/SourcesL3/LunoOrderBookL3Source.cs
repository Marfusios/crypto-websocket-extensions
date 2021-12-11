using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Luno.Client.Websocket.Client;
using Luno.Client.Websocket.Responses;

namespace Crypto.Websocket.Extensions.OrderBooks.SourcesL3
{
    /// <inheritdoc />
    public class LunoOrderBookL3Source : OrderBookSourceBase
    {
        ILunoMarketWebsocketClient _client;
        IDisposable _snapshotSubscription;
        IDisposable _diffSubscription;
        long _messageSequence;

        readonly IDictionary<string, Order> _asks = new Dictionary<string, Order>();
        readonly IDictionary<string, Order> _bids = new Dictionary<string, Order>();

        /// <inheritdoc />
        public LunoOrderBookL3Source(ILunoMarketWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "luno";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(ILunoMarketWebsocketClient client)
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

        void Subscribe()
        {
            _snapshotSubscription = _client.Streams.OrderBookSnapshotStream.Subscribe(HandleSnapshot);
            _diffSubscription = _client.Streams.OrderBookDiffStream
                .Select(response => Observable.FromAsync(() => HandleDiff(response)))
                .Concat()
                .Subscribe();
        }

        void HandleSnapshot(OrderBookSnapshotResponse response)
        {
            _messageSequence = response.Sequence;

            _asks.Clear();
            foreach (var order in response.Asks)
                _asks.Add(order.Id, order);

            _bids.Clear();
            foreach (var order in response.Bids)
                _bids.Add(order.Id, order);

            // received snapshot, convert and stream
            var levels = ConvertSnapshot(response);
            var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L3)
            {
                ExchangeName = ExchangeName,
                ServerSequence = response.Sequence,
                ServerTimestamp = response.Timestamp
            };
            StreamSnapshot(bulk);
        }

        async Task<Unit> HandleDiff(OrderBookDiffResponse response)
        {
            if (_messageSequence == 0 || response.Sequence != _messageSequence + 1)
            {
                await _client.Reconnect();
                return Unit.Default;
            }

            _messageSequence = response.Sequence;

            BufferData(response);
            return Unit.Default;
        }

        OrderBookLevel[] ConvertLevels(IReadOnlyList<Order> books, CryptoOrderSide side)
        {
            if (books == null)
                return Array.Empty<OrderBookLevel>();

            return books
                .Select(x => ConvertLevel(x, _client.Pair, side))
                .ToArray();
        }

        static OrderBookLevel ConvertLevel(Order x, string pair, CryptoOrderSide side)
        {
            return new
            (
                x.Id,
                side,
                x.Price,
                x.Volume,
                null,
                pair
            );
        }

        /// <inheritdoc />
        protected override Task<OrderBookLevelBulk> LoadSnapshotInternal(string pair, int count)
        {
            return null;
        }

        OrderBookLevel[] ConvertSnapshot(OrderBookSnapshotResponse response)
        {
            var bids = ConvertLevels(response.Bids, CryptoOrderSide.Bid);
            var asks = ConvertLevels(response.Asks, CryptoOrderSide.Ask);

            var all = bids
                .Concat(asks)
                .Where(x => x.Amount > 0)
                .ToArray();
            return all;
        }

        OrderBookLevelBulk[] ConvertDiff(OrderBookDiffResponse response)
        {
            var result = new List<OrderBookLevelBulk>();
            var toUpdate = new List<OrderBookLevel>();
            var toDelete = new List<OrderBookLevel>();

            foreach (var tradeUpdate in response.TradeUpdates)
            {
                if (_asks.ContainsKey(tradeUpdate.MakerOrderId))
                {
                    var order = _asks[tradeUpdate.MakerOrderId];
                    order.Volume -= tradeUpdate.Base;
                    if (order.Volume <= 0 || CryptoMathUtils.IsSame(order.Volume, 0))
                    {
                        _asks.Remove(tradeUpdate.MakerOrderId);
                        toDelete.Add(ConvertLevel(order, _client.Pair, CryptoOrderSide.Ask));
                    }
                    else
                        toUpdate.Add(ConvertLevel(order, _client.Pair, CryptoOrderSide.Ask));
                }
                else if (_bids.ContainsKey(tradeUpdate.MakerOrderId))
                {
                    var order = _bids[tradeUpdate.MakerOrderId];
                    order.Volume -= tradeUpdate.Base;
                    if (order.Volume <= 0 || CryptoMathUtils.IsSame(order.Volume, 0))
                    {
                        _bids.Remove(tradeUpdate.MakerOrderId);
                        toDelete.Add(ConvertLevel(order, _client.Pair, CryptoOrderSide.Bid));
                    }
                    else
                        toUpdate.Add(ConvertLevel(order, _client.Pair, CryptoOrderSide.Bid));
                }
            }

            if (toDelete.Any())
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, toDelete.ToArray(), CryptoOrderBookType.L3)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = response.Timestamp,
                    ServerSequence = response.Sequence
                };
                result.Add(bulk);
            }

            if (toUpdate.Any())
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Update, toUpdate.ToArray(), CryptoOrderBookType.L3)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = response.Timestamp,
                    ServerSequence = response.Sequence
                };
                result.Add(bulk);
            }

            if (response.CreateUpdate != null)
            {
                var level = new OrderBookLevel
                (
                    response.CreateUpdate.OrderId,
                    response.CreateUpdate.Type == "BID" ? CryptoOrderSide.Bid : CryptoOrderSide.Ask,
                    response.CreateUpdate.Price,
                    response.CreateUpdate.Volume,
                    null,
                    _client.Pair
                );
                var order = new Order
                {
                    Id = response.CreateUpdate.OrderId,
                    Price = response.CreateUpdate.Price,
                    Volume = response.CreateUpdate.Volume
                };
                switch (response.CreateUpdate.Type)
                {
                    case "ASK":
                        _asks.Add(order.Id, order);
                        break;
                    case "BID":
                        _bids.Add(order.Id, order);
                        break;
                }
                var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, new[] { level }, CryptoOrderBookType.L3)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = response.Timestamp,
                    ServerSequence = response.Sequence
                };
                result.Add(bulk);
            }

            OrderBookLevel delete = null;
            if (response.DeleteUpdate != null)
            {
                if (_asks.TryGetValue(response.DeleteUpdate.OrderId, out var ask))
                {
                    _asks.Remove(response.DeleteUpdate.OrderId);
                    delete = ConvertLevel(ask, _client.Pair, CryptoOrderSide.Ask);
                }
                else if (_bids.TryGetValue(response.DeleteUpdate.OrderId, out var bid))
                {
                    _bids.Remove(response.DeleteUpdate.OrderId);
                    delete = ConvertLevel(bid, _client.Pair, CryptoOrderSide.Bid);
                }
            }

            if (delete != null)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, new[] { delete }, CryptoOrderBookType.L3)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = response.Timestamp,
                    ServerSequence = response.Sequence
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
                var responseSafe = response as OrderBookDiffResponse;
                if (responseSafe == null)
                    continue;

                var bulks = ConvertDiff(responseSafe);

                if (!bulks.Any())
                    continue;

                result.AddRange(bulks);
            }

            return result.ToArray();
        }
    }
}
