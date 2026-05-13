using System;
using System.Globalization;
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
			var bids = response.Data.Bids;
			var asks = response.Data.Asks;
			var result = new OrderBookLevel[bids.Count + asks.Count];
			var index = 0;

			foreach (var bid in bids)
				result[index++] = ConvertLevel(bid, CryptoOrderSide.Bid, response.Data.Symbol);

			foreach (var ask in asks)
				result[index++] = ConvertLevel(ask, CryptoOrderSide.Ask, response.Data.Symbol);

			return result;
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

		private OrderBookLevelBulk[] ConvertDiff(OrderBookDeltaResponse response)
		{
			var bids = response.Data.Bids;
			var asks = response.Data.Asks;
			var maxCount = bids.Count + asks.Count;
			var updates = new OrderBookLevel[maxCount];
			var deletes = new OrderBookLevel[maxCount];
			var updateCount = 0;
			var deleteCount = 0;

			foreach (var bid in bids)
				AddDiffLevel(ConvertLevel(bid, CryptoOrderSide.Bid, response.Data.Symbol));

			foreach (var ask in asks)
				AddDiffLevel(ConvertLevel(ask, CryptoOrderSide.Ask, response.Data.Symbol));

			if (updateCount == 0 && deleteCount == 0)
				return Array.Empty<OrderBookLevelBulk>();

			var result = new OrderBookLevelBulk[(updateCount > 0 ? 1 : 0) + (deleteCount > 0 ? 1 : 0)];
			var index = 0;

			if (updateCount > 0)
			{
				Array.Resize(ref updates, updateCount);
				var bulk = new OrderBookLevelBulk(OrderBookAction.Update, updates, CryptoOrderBookType.L2);
				FillBulk(response, bulk);
				result[index++] = bulk;
			}

			if (deleteCount > 0)
			{
				Array.Resize(ref deletes, deleteCount);
				var bulk = new OrderBookLevelBulk(OrderBookAction.Delete, deletes, CryptoOrderBookType.L2);
				FillBulk(response, bulk);
				result[index] = bulk;
			}

			return result;

			void AddDiffLevel(OrderBookLevel level)
			{
				if (RecognizeAction(level) == OrderBookAction.Delete)
					deletes[deleteCount++] = level;
				else
					updates[updateCount++] = level;
			}
		}

		private void FillBulk(OrderBookResponse response, OrderBookLevelBulk bulk)
		{
			bulk.ExchangeName = ExchangeName;
			bulk.ServerTimestamp = response.CreatedTimestamp;
			bulk.ServerSequence = response.Data.Sequence;
		}

		/// <inheritdoc />
		protected override OrderBookLevelBulk[] ConvertData(object data)
		{
			return data is OrderBookDeltaResponse orderBookDeltaResponse
				? ConvertDiff(orderBookDeltaResponse)
				: Array.Empty<OrderBookLevelBulk>();
		}

		/// <inheritdoc />
		protected override OrderBookLevelBulk[] ConvertData(object[] data)
		{
			var result = new OrderBookLevelBulk[data.Length * 2];
			var count = 0;
			foreach (var response in data)
			{
				if (response is not OrderBookDeltaResponse orderBookDeltaResponse)
					continue;

				var converted = ConvertDiff(orderBookDeltaResponse);
				for (var index = 0; index < converted.Length; index++)
					result[count++] = converted[index];
			}

			if (count == result.Length)
				return result;

			Array.Resize(ref result, count);
			return result;
		}
	}
}
