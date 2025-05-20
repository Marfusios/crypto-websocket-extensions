using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Bybit.Client.Websocket.Client;
using Bybit.Client.Websocket.Responses;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
	/// <summary>
	/// Bybit order book source - based on snapshot plus diffs
	/// </summary>
	public class BybitOrderBookSource : OrderBookSourceBase
	{
		private IBybitPublicWebsocketClient _client = null!;
		private IDisposable? _snapshotSubscription;
		private IDisposable? _updateSubscription;

		/// <inheritdoc />
		public BybitOrderBookSource(IBybitPublicWebsocketClient client, ILogger logger) : base(logger)
		{
			ChangeClient(client);
		}

		/// <inheritdoc />
		public override string ExchangeName => "bybit";

		/// <summary>
		/// Change client and resubscribe to the new streams
		/// </summary>
		public void ChangeClient(IBybitPublicWebsocketClient client)
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
			_snapshotSubscription = _client.Streams.OrderBookSnapshotStream.Subscribe(HandleSnapshot);
			_updateSubscription = _client.Streams.OrderBookDeltaStream.Subscribe(HandleDelta);
		}

		private void HandleSnapshot(OrderBookSnapshotResponse response)
		{
			// received snapshot, convert and stream
			var levels = ConvertLevels(response);
			var bulk = new OrderBookLevelBulk(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
			FillBulk(response, bulk);
			StreamSnapshot(bulk);
		}

		private void HandleDelta(OrderBookDeltaResponse response)
		{
			BufferData(response);
		}

		private static OrderBookLevel[] ConvertLevels(OrderBookResponse response)
		{
			var bids = response.Data.Bids
				.Select(x => ConvertLevel(x, CryptoOrderSide.Bid, response.Data.Symbol))
				.ToArray();
			var asks = response.Data.Asks
				.Select(x => ConvertLevel(x, CryptoOrderSide.Ask, response.Data.Symbol))
				.ToArray();
			return bids.Concat(asks).ToArray();
		}

		private static OrderBookLevel ConvertLevel(double[] x, CryptoOrderSide side, string pair)
		{
			return new
			(
				x[0].ToString(CultureInfo.InvariantCulture),
				side,
				x[0],
				x[1],
				null,
				pair
			);
		}

		private static OrderBookAction RecognizeAction(OrderBookLevel level) => level.Amount > 0 ? OrderBookAction.Update : OrderBookAction.Delete;

		/// <inheritdoc />
		protected override Task<OrderBookLevelBulk?> LoadSnapshotInternal(string? pair, int count = 1000) => Task.FromResult<OrderBookLevelBulk?>(null);

		private IEnumerable<OrderBookLevelBulk> ConvertDiff(OrderBookDeltaResponse response)
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

		private void FillBulk(OrderBookResponse response, OrderBookLevelBulk bulk)
		{
			bulk.ExchangeName = ExchangeName;
			bulk.ServerTimestamp = response.CreatedTimestamp;
			bulk.ServerSequence = response.Data.Sequence;
		}

		/// <inheritdoc />
		protected override OrderBookLevelBulk[] ConvertData(object[] data)
		{
			var result = new List<OrderBookLevelBulk>();
			foreach (var response in data)
			{
				if (response is not OrderBookDeltaResponse orderBookDeltaResponse)
					continue;

				var converted = ConvertDiff(orderBookDeltaResponse);
				result.AddRange(converted);
			}

			return result.ToArray();
		}
	}
}
