using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Utils;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// Cryptocurrency order book (supports all levels L2, L3, etc).
    /// Process order book data from one source and one target pair.
    /// Only first levels are computed in advance, allocates more memory than CryptoOrderBookL2 counterpart. 
    /// </summary>
    [DebuggerDisplay("CryptoOrderBook [{TargetPair} {TargetType}] bid: {BidPrice} ({BidLevelsInternal.Count}) ask: {AskPrice} ({AskLevelsInternal.Count})")]
    public class CryptoOrderBook : CryptoOrderBookBase<OrderedDictionary>
    {
        readonly OrderBookLevelsOrderPerPrice _bidLevelOrdering = new(200);
        readonly OrderBookLevelsOrderPerPrice _askLevelOrdering = new(200);

        readonly int _priceLevelInitialCapacity = 2;

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
        public override OrderBookLevel FindBidLevelByPrice(double price)
        {
            lock (Locker)
            {
                return BidLevelsInternal.TryGetValue(price, out var group)
                    ? group.Values.OfType<OrderBookLevel>().FirstOrDefault()
                    : null;
            }
        }

        /// <inheritdoc />
        public override OrderBookLevel[] FindBidLevelsByPrice(double price)
        {
            lock (Locker)
            {
                return BidLevelsInternal.TryGetValue(price, out var group)
                    ? group.Values.OfType<OrderBookLevel>().ToArray()
                    : Array.Empty<OrderBookLevel>();
            }
        }

        /// <inheritdoc />
        public override OrderBookLevel FindAskLevelByPrice(double price)
        {
            lock (Locker)
            {
                return AskLevelsInternal.TryGetValue(price, out var group)
                    ? group.Values.OfType<OrderBookLevel>().FirstOrDefault()
                    : null;
            }
        }

        /// <inheritdoc />
        public override OrderBookLevel[] FindAskLevelsByPrice(double price)
        {
            lock (Locker)
            {
                return AskLevelsInternal.TryGetValue(price, out var group)
                    ? group.Values.OfType<OrderBookLevel>().ToArray()
                    : Array.Empty<OrderBookLevel>();
            }
        }

        /// <inheritdoc />
        protected override bool IsForThis(OrderBookLevelBulk bulk)
        {
            if (TargetType == CryptoOrderBookType.All)
            {
                // handling everything, do not filter out
                return true;
            }

            // handle only same type as selected
            return TargetType == bulk.OrderBookType;
        }

        /// <inheritdoc />
        protected override void UpdateSnapshot(L2Snapshot snapshot)
        {
            snapshot.Update(BidPrice, AskPrice, BidAmount, AskAmount);
            
            if (snapshot.Bids.Any())
                UpdateQuotes(snapshot.Bids, BidLevelsInternal.Take(snapshot.Bids.Count).ToList());
            
            if (snapshot.Asks.Any())
                UpdateQuotes(snapshot.Asks, AskLevelsInternal.Take(snapshot.Asks.Count).ToList());

            static void UpdateQuotes(IEnumerable<CryptoQuote> quotes, IReadOnlyList<KeyValuePair<double, OrderedDictionary>> levels)
            {
                var index = 0;
                foreach (var quote in quotes)
                {
                    if (index < levels.Count)
                    {
                        quote.Price = levels[index].Key;
                        quote.Amount = levels[index].Value.Values.Cast<OrderBookLevel>().Sum(x => x.Amount ?? 0);
                    }
                    else
                    {
                        quote.Price = 0;
                        quote.Amount = 0;
                    }

                    index++;
                }
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
            foreach (var level in levels)
            {
                if (level.Side == CryptoOrderSide.Undefined)
                    continue;

                var collection = GetLevelsCollection(level.Side);
                var ordering = GetLevelsOrdering(level.Side);

                var existing = FindLevelById(level.Id, level.Side);
                if (existing == null)
                {
                    level.AmountDifference = level.Amount ?? 0;
                    level.CountDifference = level.Count ?? 0;

                    InsertToCollection(collection, ordering, level);
                    continue;
                }

                var previousPrice = existing.Price;
                var previousAmount = existing.Amount;

                ComputeUpdate(existing, level);

                InsertToCollection(collection, ordering, existing, previousPrice, previousAmount);
            }
        }

        void InsertToCollection(IDictionary<double, OrderedDictionary> collection, OrderBookLevelsOrderPerPrice ordering,
            OrderBookLevel level, double? previousPrice = null, double? previousAmount = null)
        {
            if (collection == null)
                return;

            if (IsInvalidLevel(level))
            {
                LogDebug($"Received weird level, ignoring. [{level.Side}] Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                return;
            }

            GetAllCollection(level.Side)[level.Id] = level;
            InsertLevelIntoPriceGroup(level, collection, ordering, previousPrice, previousAmount);
        }

        void InsertLevelIntoPriceGroup(OrderBookLevel level, IDictionary<double, OrderedDictionary> collection,
            OrderBookLevelsOrderPerPrice orderingGroup, double? previousPrice = null, double? previousAmount = null)
        {
            // remove from last location if needed (price updated)
            if (previousPrice.HasValue && !CryptoMathUtils.IsSame(previousPrice, level.Price) && collection.ContainsKey(previousPrice.Value))
            {
                var previousPriceVal = previousPrice.Value;
                var previousGroup = collection[previousPriceVal];
                previousGroup.Remove(level.Id);

                if (previousGroup.Count <= 0)
                {
                    collection.Remove(previousPriceVal);
                    orderingGroup.Remove(previousPriceVal);
                }

                level.PriceUpdatedCount++;
            }

            // ReSharper disable once PossibleInvalidOperationException
            var price = level.Price.Value;

            if (!orderingGroup.ContainsKey(price))
                orderingGroup[price] = 0;

            if (!collection.ContainsKey(price))
            {
                collection[price] = new OrderedDictionary(_priceLevelInitialCapacity) { { level.Id, level } };
                return;
            }

            var currentGroup = collection[price];
            var ordering = orderingGroup[price] + 1;
            orderingGroup[price] = ordering;

            // we wanna put order at the end of queue
            if (previousPrice.HasValue)
                currentGroup.Remove(level.Id);

            // amount changed, increase counter
            if (previousAmount.HasValue && !CryptoMathUtils.IsSame(previousAmount, level.Amount))
                level.AmountUpdatedCount++;

            currentGroup[level.Id] = level;
            level.Ordering = ordering;
        }

        /// <inheritdoc />
        protected override void DeleteLevels(IReadOnlyCollection<OrderBookLevel> levels)
        {
            FillCurrentIndex(levels);

            foreach (var level in levels)
            {
                if (level.Side == CryptoOrderSide.Undefined)
                    continue;

                var price = level.Price ?? -1;
                var id = level.Id;
                var collection = GetLevelsCollection(level.Side);
                var ordering = GetLevelsOrdering(level.Side);
                var allLevels = GetAllCollection(level.Side);

                if (allLevels.ContainsKey(level.Id))
                {
                    var existing = allLevels[level.Id];

                    ComputeDelete(existing, level);

                    price = existing.Price ?? -1;
                    allLevels.Remove(level.Id);
                }

                if (collection.ContainsKey(price))
                {
                    var group = collection[price];
                    if (group.Contains(id))
                        group.Remove(id);

                    if (group.Count <= 0)
                    {
                        collection.Remove(price);
                        ordering.Remove(price);
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override OrderBookLevel GetFirstBid()
        {
            var bids = BidLevelsInternal.Values.FirstOrDefault();
            return bids?.Count > 0 ? (OrderBookLevel)bids[0] : null;
        }

        /// <inheritdoc />
        protected override OrderBookLevel GetFirstAsk()
        {
            var asks = AskLevelsInternal.Values.FirstOrDefault();
            return asks?.Count > 0 ? (OrderBookLevel)asks[0] : null;
        }

        /// <inheritdoc />
        protected override OrderBookLevel[] ComputeBidLevels()
        {
            lock (Locker)
            {
                return BidLevelsInternal.Values.SelectMany(x => x.Values.Cast<OrderBookLevel>()).ToArray();
            }
        }

        /// <inheritdoc />
        protected override OrderBookLevel[] ComputeAskLevels()
        {
            lock (Locker)
            {
                return AskLevelsInternal.Values.SelectMany(x => x.Values.Cast<OrderBookLevel>()).ToArray();
            }
        }

        IReadOnlyDictionary<double, OrderBookLevel[]> ComputeLevelsPerPrice(SortedList<double, OrderedDictionary> levels)
        {
            lock (Locker)
            {
                return levels.ToDictionary(x => x.Key, y => y.Value.Values.Cast<OrderBookLevel>().ToArray());
            }
        }

        OrderBookLevelsOrderPerPrice GetLevelsOrdering(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;

            return side == CryptoOrderSide.Bid
                ? _bidLevelOrdering
                : _askLevelOrdering;
        }
    }
}
