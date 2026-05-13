using System;
using System.Collections.Generic;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// Cryptocurrency order book optimized for L2 precision (grouped by price).
    /// Process order book data from one source and one target pair.
    /// Only first levels are computed in advance, allocates less memory than CryptoOrderBook counterpart.
    /// </summary>
    [DebuggerDisplay("CryptoOrderBook [{TargetPair}] bid: {BidPrice} ({BidLevelsInternal.Count}) ask: {AskPrice} ({AskLevelsInternal.Count})")]
    public class CryptoOrderBookL2 : CryptoOrderBookBase<OrderBookLevel>
    {
        /// <summary>
        /// Cryptocurrency order book.
        /// Process order book data from one source per one target pair. 
        /// </summary>
        /// <param name="targetPair">Select target pair</param>
        /// <param name="source">Provide level 2 source data</param>
        public CryptoOrderBookL2(string targetPair, IOrderBookSource source) : base(targetPair, source)
        {
            TargetType = CryptoOrderBookType.L2;
            Initialize();
        }

        /// <inheritdoc />
        public override OrderBookLevel? FindBidLevelByPrice(double price)
        {
            lock (Locker)
            {
                return BidLevelsInternal.GetValueOrDefault(price);
            }
        }

        /// <inheritdoc />
        public override OrderBookLevel[] FindBidLevelsByPrice(double price)
        {
            var level = FindBidLevelByPrice(price);
            return level == null
                ? Array.Empty<OrderBookLevel>()
                : new[] { level };
        }

        /// <inheritdoc />
        public override OrderBookLevel? FindAskLevelByPrice(double price)
        {
            lock (Locker)
            {
                return AskLevelsInternal.GetValueOrDefault(price);
            }
        }

        /// <inheritdoc />
        public override OrderBookLevel[] FindAskLevelsByPrice(double price)
        {
            var level = FindAskLevelByPrice(price);
            return level == null
                ? Array.Empty<OrderBookLevel>()
                : new[] { level };
        }

        /// <inheritdoc />
        protected override bool IsForThis(OrderBookLevelBulk? bulk) => bulk?.OrderBookType is CryptoOrderBookType.L1 or CryptoOrderBookType.L2;

        /// <inheritdoc />
        protected override void UpdateSnapshot(L2Snapshot snapshot)
        {
            snapshot.Update(BidPrice, AskPrice, BidAmount, AskAmount);

            if (snapshot.Bids.Count > 0)
                UpdateQuotes(snapshot.Bids, BidLevelsInternal.Values);

            if (snapshot.Asks.Count > 0)
                UpdateQuotes(snapshot.Asks, AskLevelsInternal.Values);

            static void UpdateQuotes(IReadOnlyList<CryptoQuote> quotes, IList<OrderBookLevel> levels)
            {
                for (var index = 0; index < quotes.Count; index++)
                {
                    var quote = quotes[index];
                    if (index < levels.Count)
                    {
                        quote.Price = levels[index].Price ?? 0;
                        quote.Amount = levels[index].Amount ?? 0;
                    }
                    else
                    {
                        quote.Price = 0;
                        quote.Amount = 0;
                    }

                }
            }
        }

        /// <inheritdoc />
        protected override void ClearLevels()
        {
            BidLevelsInternal.Clear();
            AskLevelsInternal.Clear();
            AllBidLevels.Clear();
            AllAskLevels.Clear();
        }

        /// <inheritdoc />
        protected override void HandleSnapshotBidLevel(OrderBookLevel level) => BidLevelsInternal[level.Price!.Value] = level;

        /// <inheritdoc />
        protected override void HandleSnapshotAskLevel(OrderBookLevel level) => AskLevelsInternal[level.Price!.Value] = level;

        /// <inheritdoc />
        protected override void UpdateLevels(IEnumerable<OrderBookLevel> levels)
        {
            if (levels is IReadOnlyList<OrderBookLevel> list)
            {
                UpdateLevels(list);
                return;
            }

            foreach (var level in levels)
                UpdateLevel(level);
        }

        /// <inheritdoc />
        protected override void UpdateLevels(IReadOnlyList<OrderBookLevel> levels)
        {
            for (var index = 0; index < levels.Count; index++)
                UpdateLevel(levels[index]);
        }

        private void UpdateLevel(OrderBookLevel level)
        {
            var collection = GetLevelsCollection(level.Side);
            if (collection == null)
                return;

            var allLevels = GetAllCollection(level.Side);
            if (!allLevels.TryGetValue(level.Id, out var existing))
            {
                level.AmountDifference = level.Amount ?? 0;
                level.CountDifference = level.Count ?? 0;
                level.AmountUpdatedCount = 0;

                InsertToCollection(collection, allLevels, level);
                return;
            }

            CalculateMetricsForUpdatedLevel(existing, level);

            InsertToCollection(collection, allLevels, existing);
        }

        private void InsertToCollection(IDictionary<double, OrderBookLevel> collection, OrderBookLevelsById allLevels, OrderBookLevel level)
        {
            if (IsInvalidLevel(level))
            {
                LogDebug($"Received weird level, ignoring. Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                return;
            }

            // ReSharper disable once PossibleInvalidOperationException
            collection[level.Price!.Value] = level;
            allLevels[level.Id] = level;
        }

        /// <inheritdoc />
        protected override void DeleteLevels(IReadOnlyCollection<OrderBookLevel> levels)
        {
            FillCurrentIndex(levels);

            foreach (var level in levels)
                DeleteLevel(level);
        }

        /// <inheritdoc />
        protected override void DeleteLevels(IReadOnlyList<OrderBookLevel> levels)
        {
            FillCurrentIndex(levels);

            for (var index = 0; index < levels.Count; index++)
                DeleteLevel(levels[index]);
        }

        private void DeleteLevel(OrderBookLevel level)
        {
            var collection = GetLevelsCollection(level.Side);
            if (collection == null)
                return;

            var price = level.Price ?? -1;
            var allLevels = GetAllCollection(level.Side);
            OrderBookLevel? existing = null;

            if (collection.TryGetValue(price, out existing))
            {
                collection.Remove(price);
            }
            else if (allLevels.TryGetValue(level.Id, out existing))
            {
                var existingPrice = existing.Price ?? -1;
                collection.Remove(existingPrice);
            }

            allLevels.Remove(level.Id);

            if (existing != null)
                CalculateMetricsForDeletedLevel(existing, level);
        }

        /// <inheritdoc />
        protected override OrderBookLevel? GetFirstBid() => BidLevelsInternal.Count > 0 ? BidLevelsInternal.Values[0] : null;

        /// <inheritdoc />
        protected override OrderBookLevel? GetFirstAsk() => AskLevelsInternal.Count > 0 ? AskLevelsInternal.Values[0] : null;

        /// <inheritdoc />
        protected override OrderBookLevel[] ComputeBidLevels()
        {
            lock (Locker)
            {
                return CopyLevels(BidLevelsInternal.Values);
            }
        }

        /// <inheritdoc />
        protected override OrderBookLevel[] ComputeAskLevels()
        {
            lock (Locker)
            {
                return CopyLevels(AskLevelsInternal.Values);
            }
        }

        private static OrderBookLevel[] CopyLevels(IList<OrderBookLevel> levels)
        {
            if (levels.Count == 0)
                return Array.Empty<OrderBookLevel>();

            var result = new OrderBookLevel[levels.Count];
            for (var index = 0; index < levels.Count; index++)
                result[index] = levels[index];

            return result;
        }
    }
}
