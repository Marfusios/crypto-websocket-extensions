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
using Microsoft.Extensions.Logging;
using Valr.Client.Websocket.Client;
using Valr.Client.Websocket.Responses;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
	/// <summary>
	/// Valr order book source - based on snapshot plus diffs
	/// </summary>
	public class ValrOrderBookSource : OrderBookSourceBase
	{
		private IValrTradeWebsocketClient _client = null!;
		private IDisposable? _snapshotSubscription;
		private IDisposable? _updateSubscription;

		/// <inheritdoc />
		public ValrOrderBookSource(IValrTradeWebsocketClient client, ILogger logger) : base(logger)
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
			_updateSubscription?.Dispose();
			Subscribe();
		}

		/// <inheritdoc />
		public override void Dispose()
		{
			base.Dispose();
			_snapshotSubscription?.Dispose();
		}

		private void Subscribe()
		{
			_snapshotSubscription = _client.Streams.L1OrderBookSnapshotStream.Subscribe(HandleSnapshot);
			_updateSubscription = _client.Streams.L1OrderBookUpdateStream.Subscribe(HandleUpdate);
		}

		private void HandleSnapshot(L1TrackedOrderBookResponse response)
		{
			// received snapshot, convert and stream
			var levels = ConvertLevels(response);
			var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
			FillBulk(response, bulk);
			StreamSnapshot(bulk);
		}

		private void HandleUpdate(L1TrackedOrderBookResponse response)
		{
			BufferData(response);
		}

		private static OrderBookLevel[] ConvertLevels(L1TrackedOrderBookResponse response)
		{
			var bids = response.Data.Bids
				.Select(x => ConvertLevel(x, CryptoOrderSide.Bid, response.CurrencyPairSymbol))
				.ToArray();
			var asks = response.Data.Asks
				.Select(x => ConvertLevel(x, CryptoOrderSide.Ask, response.CurrencyPairSymbol))
				.ToArray();
			return bids.Concat(asks).ToArray();
		}

		private static OrderBookLevel ConvertLevel(L1Quote x, CryptoOrderSide side, string pair)
		{
			return new
			(
				x.Price.ToString(CultureInfo.InvariantCulture),
				side,
				x.Price,
				x.Quantity,
				null,
				pair
			);
		}

		private OrderBookAction RecognizeAction(OrderBookLevel level)
		{
			if (level.Amount > 0)
				return OrderBookAction.Update;
			return OrderBookAction.Delete;
		}

		/// <inheritdoc />
		protected override Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000)
		{
			return Task.FromResult<OrderBookLevelBulk?>(null);
		}

		private IEnumerable<OrderBookLevelBulk> ConvertDiff(L1TrackedOrderBookResponse response)
		{
			var levels = ConvertLevels(response);
			var group = levels.GroupBy(RecognizeAction).ToArray();
			foreach (var actionGroup in group)
			{
				var bulk = new OrderBookLevelBulk(actionGroup.Key, actionGroup.ToArray(), CryptoOrderBookType.L2);
				FillBulk(response, bulk);
				yield return bulk;
			}
		}

		private void FillBulk(L1TrackedOrderBookResponse response, OrderBookLevelBulk bulk)
		{
			bulk.ExchangeName = ExchangeName;
			bulk.ServerTimestamp = response.Data.LastChange;
			bulk.ServerSequence = response.Data.SequenceNumber;
		}

		/// <inheritdoc />
		protected override OrderBookLevelBulk[] ConvertData(object[] data)
		{
			var result = new List<OrderBookLevelBulk>();
			foreach (var response in data)
			{
				var responseSafe = response as L1TrackedOrderBookResponse;
				if (responseSafe == null)
					continue;

				var converted = ConvertDiff(responseSafe);
				result.AddRange(converted);
			}

			return result.ToArray();
		}
	}
}
