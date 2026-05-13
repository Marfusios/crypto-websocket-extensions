using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Utils;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// Cryptocurrency order book (supports all levels L2, L3, etc.).
    /// Process order book data from one source and one target pair.
    /// Only first levels are computed in advance, allocates more memory than CryptoOrderBookL2 counterpart. 
    /// </summary>
    [DebuggerDisplay("CryptoOrderBook [{TargetPair} {TargetType}] bid: {BidPrice} ({BidLevelsInternal.Count}) ask: {AskPrice} ({AskLevelsInternal.Count})")]
    public class CryptoOrderBook : CryptoOrderBookBase<OrderedDictionary>
    {
        private readonly OrderBookLevelsOrderPerPrice _bidLevelOrdering = new(200);
        private readonly OrderBookLevelsOrderPerPrice _askLevelOrdering = new(200);

        private readonly int _priceLevelInitialCapacity = 2;

        /// <summary>
        /// Cryptocurrency order book.
        /// Process order book data from one source per one target pair. 
        /// </summary>
        /// <param name="targetPair">Select target pair</param>
        /// <param name="source">Provide order book data source</param>
        /// <param name="targetType">Select target precision (default: All - accepts every type of data)</param>
        public CryptoOrderBook(string targetPair, IOrderBookSource source, CryptoOrderBookType targetType = CryptoOrderBookType.All) : base(targetPair, source)
        {
            TargetType = targetType;

            if (targetType is CryptoOrderBookType.L1 or CryptoOrderBookType.L2)
            {
                // save memory, only one order at price level
                _priceLevelInitialCapacity = 1;
            }
            else if (targetType is CryptoOrderBookType.L3)
            {
                // better performance, could be multiple orders at price level
                _priceLevelInitialCapacity = 5;
            }

            Initialize();
        }

        /// <summary>
        /// Current bid side of the order book grouped by price (ordered from higher to lower price)
        /// </summary>
        public IReadOnlyDictionary<double, OrderBookLevel[]> BidLevelsPerPrice => ComputeLevelsPerPrice(BidLevelsInternal);

        /// <summary>
        /// Current ask side of the order book grouped by price (ordered from lower to higher price)
        /// </summary>
        public IReadOnlyDictionary<double, OrderBookLevel[]> AskLevelsPerPrice => ComputeLevelsPerPrice(AskLevelsInternal);

        /// <inheritdoc />
        public override OrderBookLevel? FindBidLevelByPrice(double price)
        {
            lock (Locker)
            {
                return BidLevelsInternal.TryGetValue(price, out var group)
                    ? GetFirstLevel(group)
                    : null;
            }
        }

        /// <inheritdoc />
        public override OrderBookLevel[] FindBidLevelsByPrice(double price)
        {
            lock (Locker)
            {
                return BidLevelsInternal.TryGetValue(price, out var group)
                    ? ToLevelArray(group)
                    : Array.Empty<OrderBookLevel>();
            }
        }

        /// <inheritdoc />
        public override OrderBookLevel? FindAskLevelByPrice(double price)
        {
            lock (Locker)
            {
                return AskLevelsInternal.TryGetValue(price, out var group)
                    ? GetFirstLevel(group)
                    : null;
            }
        }

        /// <inheritdoc />
        public override OrderBookLevel[] FindAskLevelsByPrice(double price)
        {
            lock (Locker)
            {
                return AskLevelsInternal.TryGetValue(price, out var group)
                    ? ToLevelArray(group)
                    : Array.Empty<OrderBookLevel>();
            }
        }

        /// <inheritdoc />
        protected override bool IsForThis(OrderBookLevelBulk? bulk)
        {
            if (TargetType == CryptoOrderBookType.All)
            {
                // handling everything, do not filter out
                return true;
            }

            // handle only same type as selected
            return TargetType == bulk?.OrderBookType;
        }

        /// <inheritdoc />
        protected override void UpdateSnapshot(L2Snapshot snapshot)
        {
            snapshot.Update(BidPrice, AskPrice, BidAmount, AskAmount);

            if (snapshot.Bids.Count > 0)
                UpdateQuotes(snapshot.Bids, BidLevelsInternal);

            if (snapshot.Asks.Count > 0)
                UpdateQuotes(snapshot.Asks, AskLevelsInternal);

            static void UpdateQuotes(IReadOnlyList<CryptoQuote> quotes, SortedList<double, OrderedDictionary> levels)
            {
                for (var index = 0; index < quotes.Count; index++)
                {
                    var quote = quotes[index];
                    if (index < levels.Count)
                    {
                        quote.Price = levels.Keys[index];
                        quote.Amount = SumAmount(levels.Values[index]);
                    }
                    else
                    {
                        quote.Price = 0;
                        quote.Amount = 0;
                    }

                }
            }

            static double SumAmount(OrderedDictionary group)
            {
                double amount = 0;
                for (var index = 0; index < group.Count; index++)
                {
                    if (group[index] is OrderBookLevel level)
                        amount += level.Amount ?? 0;
                }

                return amount;
            }
        }

        /// <inheritdoc />
        protected override void ClearLevels()
        {
            BidLevelsInternal.Clear();
            _bidLevelOrdering.Clear();
            AskLevelsInternal.Clear();
            _askLevelOrdering.Clear();
            AllBidLevels.Clear();
            AllAskLevels.Clear();
        }

        /// <inheritdoc />
        protected override void HandleSnapshotBidLevel(OrderBookLevel level) => InsertLevelIntoPriceGroup(level, BidLevelsInternal, _bidLevelOrdering);

        /// <inheritdoc />
        protected override void HandleSnapshotAskLevel(OrderBookLevel level) => InsertLevelIntoPriceGroup(level, AskLevelsInternal, _askLevelOrdering);

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
            if (level.Side == CryptoOrderSide.Undefined)
                return;

            var collection = GetLevelsCollection(level.Side);
            var ordering = GetLevelsOrdering(level.Side);
            var allLevels = GetAllCollection(level.Side);

            if (!allLevels.TryGetValue(level.Id, out var existing))
            {
                level.AmountDifference = level.Amount ?? 0;
                level.CountDifference = level.Count ?? 0;

                InsertToCollection(collection, ordering, allLevels, level);
                return;
            }

            var previousPrice = existing.Price;

            CalculateMetricsForUpdatedLevel(existing, level);

            InsertToCollection(collection, ordering, allLevels, existing, previousPrice);
        }

        private void InsertToCollection(IDictionary<double, OrderedDictionary>? collection, OrderBookLevelsOrderPerPrice? ordering,
            OrderBookLevelsById allLevels, OrderBookLevel level, double? previousPrice = null)
        {
            if (collection == null)
                return;
            if (IsInvalidLevel(level))
            {
                LogDebug($"Received weird level, ignoring. " +
                         $"[{level.Side}] Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                return;
            }

            allLevels[level.Id] = level;
            InsertLevelIntoPriceGroup(level, collection, ordering, previousPrice);
        }

        private void InsertLevelIntoPriceGroup(OrderBookLevel level, IDictionary<double, OrderedDictionary> collection,
            OrderBookLevelsOrderPerPrice? orderingGroup, double? previousPrice = null)
        {
            if (orderingGroup == null || level.Price == null)
                return;

            // remove from last location if needed (price updated)
            if (previousPrice.HasValue &&
                !CryptoMathUtils.IsSame(previousPrice, level.Price) &&
                collection.TryGetValue(previousPrice.Value, out var previousGroup))
            {
                var previousPriceVal = previousPrice.Value;
                previousGroup.Remove(level.Id);

                if (previousGroup.Count <= 0)
                {
                    collection.Remove(previousPriceVal);
                    orderingGroup.Remove(previousPriceVal);
                }

                level.PriceUpdatedCount++;
            }

            var price = level.Price.Value;

            orderingGroup.TryAdd(price, 0);

            if (!collection.TryGetValue(price, out var currentGroup))
            {
                collection[price] = new OrderedDictionary(_priceLevelInitialCapacity) { { level.Id, level } };
                return;
            }

            var ordering = orderingGroup[price] + 1;
            orderingGroup[price] = ordering;

            // we wanna put order at the end of queue
            if (previousPrice.HasValue)
            {
                currentGroup.Remove(level.Id);
            }

            currentGroup[level.Id] = level;
            level.Ordering = ordering;
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
            if (level.Side == CryptoOrderSide.Undefined)
                return;

            var price = level.Price ?? -1;
            var id = level.Id;
            var collection = GetLevelsCollection(level.Side);
            var ordering = GetLevelsOrdering(level.Side);
            var allLevels = GetAllCollection(level.Side);

            if (allLevels.TryGetValue(level.Id, out var existing))
            {
                CalculateMetricsForDeletedLevel(existing, level);

                price = existing.Price ?? -1;
                allLevels.Remove(level.Id);
            }

            if (collection != null && collection.TryGetValue(price, out var group))
            {
                group.Remove(id);

                if (group.Count <= 0)
                {
                    collection.Remove(price);
                    ordering?.Remove(price);
                }
            }
        }

        /// <inheritdoc />
        protected override OrderBookLevel? GetFirstBid()
        {
            if (BidLevelsInternal.Count == 0)
                return null;

            var bids = BidLevelsInternal.Values[0];
            return bids.Count > 0 ? (OrderBookLevel?)bids[0] : null;
        }

        /// <inheritdoc />
        protected override OrderBookLevel? GetFirstAsk()
        {
            if (AskLevelsInternal.Count == 0)
                return null;

            var asks = AskLevelsInternal.Values[0];
            return asks.Count > 0 ? (OrderBookLevel?)asks[0] : null;
        }

        /// <inheritdoc />
        protected override OrderBookLevel[] ComputeBidLevels()
        {
            lock (Locker)
            {
                return FlattenLevels(BidLevelsInternal);
            }
        }

        /// <inheritdoc />
        protected override OrderBookLevel[] ComputeAskLevels()
        {
            lock (Locker)
            {
                return FlattenLevels(AskLevelsInternal);
            }
        }

        private IReadOnlyDictionary<double, OrderBookLevel[]> ComputeLevelsPerPrice(SortedList<double, OrderedDictionary> levels)
        {
            lock (Locker)
            {
                var result = new Dictionary<double, OrderBookLevel[]>(levels.Count);
                foreach (var level in levels)
                    result[level.Key] = ToLevelArray(level.Value);

                return result;
            }
        }

        private static OrderBookLevel? GetFirstLevel(OrderedDictionary group)
        {
            for (var index = 0; index < group.Count; index++)
            {
                if (group[index] is OrderBookLevel level)
                    return level;
            }

            return null;
        }

        private static OrderBookLevel[] FlattenLevels(SortedList<double, OrderedDictionary> levels)
        {
            var count = 0;
            foreach (var group in levels.Values)
                count += group.Count;

            if (count == 0)
                return Array.Empty<OrderBookLevel>();

            var result = new OrderBookLevel[count];
            var index = 0;
            foreach (var group in levels.Values)
                CopyLevels(group, result, ref index);

            if (index != result.Length)
                Array.Resize(ref result, index);

            return result;
        }

        private static OrderBookLevel[] ToLevelArray(OrderedDictionary group)
        {
            if (group.Count == 0)
                return Array.Empty<OrderBookLevel>();

            var result = new OrderBookLevel[group.Count];
            var index = 0;
            CopyLevels(group, result, ref index);

            if (index != result.Length)
                Array.Resize(ref result, index);

            return result;
        }

        private static void CopyLevels(OrderedDictionary group, OrderBookLevel[] result, ref int index)
        {
            for (var groupIndex = 0; groupIndex < group.Count; groupIndex++)
            {
                if (group[groupIndex] is OrderBookLevel level)
                    result[index++] = level;
            }
        }

        private OrderBookLevelsOrderPerPrice? GetLevelsOrdering(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;

            return side == CryptoOrderSide.Bid
                ? _bidLevelOrdering
                : _askLevelOrdering;
        }
    }
}
