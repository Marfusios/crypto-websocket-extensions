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
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.OrderBooks.SourcesL3
{
	/// <inheritdoc />
	public class LunoOrderBookL3Source : OrderBookSourceBase
	{
		private ILunoMarketWebsocketClient _client = null!;
		private IDisposable? _snapshotSubscription;
		private IDisposable? _diffSubscription;
		private long _messageSequence;

		private readonly IDictionary<string, Order> _asks = new Dictionary<string, Order>();
		private readonly IDictionary<string, Order> _bids = new Dictionary<string, Order>();

		/// <inheritdoc />
		public LunoOrderBookL3Source(ILunoMarketWebsocketClient client, ILogger logger) : base(logger)
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

		private void Subscribe()
		{
			_snapshotSubscription = _client.Streams.OrderBookSnapshotStream.Subscribe(HandleSnapshot);
			_diffSubscription = _client.Streams.OrderBookDiffStream
				.Select(diff => Observable.FromAsync(() => HandleDiff(diff)))
				.Concat()
				.Subscribe();
		}

		private void HandleSnapshot(OrderBookSnapshot snapshot)
		{
			_messageSequence = snapshot.Sequence;

			_asks.Clear();
			foreach (var order in snapshot.Asks)
				_asks.Add(order.Id, order);

			_bids.Clear();
			foreach (var order in snapshot.Bids)
				_bids.Add(order.Id, order);

			// received snapshot, convert and stream
			var levels = ConvertSnapshot(snapshot);
			var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L3)
			{
				ExchangeName = ExchangeName,
				ServerSequence = snapshot.Sequence,
				ServerTimestamp = snapshot.Timestamp
			};
			StreamSnapshot(bulk);
		}

		private async Task<Unit> HandleDiff(OrderBookDiff diff)
		{
			if (_messageSequence == 0 || diff.Sequence != _messageSequence + 1)
			{
				await _client.Reconnect();
				return Unit.Default;
			}

			_messageSequence = diff.Sequence;

			BufferData(diff);
			return Unit.Default;
		}

		private OrderBookLevel[] ConvertLevels(IReadOnlyList<Order> levels, CryptoOrderSide side)
		{
			return [.. levels.Select(x => ConvertLevel(x, _client.Pair, side))];
		}

		private static OrderBookLevel ConvertLevel(Order x, string pair, CryptoOrderSide side)
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
		protected override Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000)
		{
			return Task.FromResult<OrderBookLevelBulk?>(null);
		}

		OrderBookLevel[] ConvertSnapshot(OrderBookSnapshot snapshot)
		{
			var bids = ConvertLevels(snapshot.Bids, CryptoOrderSide.Bid);
			var asks = ConvertLevels(snapshot.Asks, CryptoOrderSide.Ask);

			var all = bids
				.Concat(asks)
				.Where(x => x.Amount > 0)
				.ToArray();
			return all;
		}

		OrderBookLevelBulk[] ConvertDiff(OrderBookDiff diff)
		{
			var result = new List<OrderBookLevelBulk>();

			HandleTrades(diff, result);
			HandleCreate(diff, result);
			HandleDelete(diff, result);

			return result.ToArray();
		}

        void HandleTrades(OrderBookDiff diff, List<OrderBookLevelBulk> result)
		{
			var toUpdate = new List<OrderBookLevel>();
            var toDelete = new List<OrderBookLevel>();

			foreach (var tradeUpdate in diff.TradeUpdates)
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

            if (toDelete.Count > 0)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, [.. toDelete], CryptoOrderBookType.L3)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = diff.Timestamp,
                    ServerSequence = diff.Sequence
                };
                result.Add(bulk);
            }

            if (toUpdate.Count > 0)
            {
                var bulk = new OrderBookLevelBulk(OrderBookAction.Update, [.. toUpdate], CryptoOrderBookType.L3)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = diff.Timestamp,
                    ServerSequence = diff.Sequence
                };
                result.Add(bulk);
            }
		}

        void HandleCreate(OrderBookDiff diff, List<OrderBookLevelBulk> result)
        {
            if (diff.CreateUpdate != null)
            {
                var level = new OrderBookLevel
                (
                    diff.CreateUpdate.OrderId,
                    diff.CreateUpdate.Type == "BID" ? CryptoOrderSide.Bid : CryptoOrderSide.Ask,
                    diff.CreateUpdate.Price,
                    diff.CreateUpdate.Volume,
                    null,
                    _client.Pair
                );

                var order = new Order
                {
                    Id = diff.CreateUpdate.OrderId,
                    Price = diff.CreateUpdate.Price,
                    Volume = diff.CreateUpdate.Volume
                };

                switch (diff.CreateUpdate.Type)
                {
                    case "ASK":
                        _asks.Add(order.Id, order);
                        break;
                    case "BID":
                        _bids.Add(order.Id, order);
                        break;
                }

                var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, [level], CryptoOrderBookType.L3)
                {
                    ExchangeName = ExchangeName,
                    ServerTimestamp = diff.Timestamp,
                    ServerSequence = diff.Sequence
                };
                result.Add(bulk);
            }
        }

        void HandleDelete(OrderBookDiff diff, List<OrderBookLevelBulk> result)
		{
			OrderBookLevel? delete = null;
			if (diff.DeleteUpdate != null)
			{
				if (_asks.Remove(diff.DeleteUpdate.OrderId, out var ask))
				{
					delete = ConvertLevel(ask, _client.Pair, CryptoOrderSide.Ask);
				}
				else if (_bids.Remove(diff.DeleteUpdate.OrderId, out var bid))
				{
					delete = ConvertLevel(bid, _client.Pair, CryptoOrderSide.Bid);
				}
			}

			if (delete != null)
			{
				var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, [delete], CryptoOrderBookType.L3)
				{
					ExchangeName = ExchangeName,
					ServerTimestamp = diff.Timestamp,
					ServerSequence = diff.Sequence
				};
				result.Add(bulk);
			}
		}

		/// <inheritdoc />
		protected override OrderBookLevelBulk[] ConvertData(object[] data)
		{
			var result = new List<OrderBookLevelBulk>();
			foreach (var response in data)
			{
				var responseSafe = response as OrderBookDiff;
				if (responseSafe == null)
					continue;

				var bulks = ConvertDiff(responseSafe);

				if (bulks.Length == 0)
					continue;

				result.AddRange(bulks);
			}

			return [.. result];
		}
	}
}
