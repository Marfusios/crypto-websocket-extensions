using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Crypto.Websocket.Extensions.Core.Models;
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
        public override OrderBookLevel FindBidLevelByPrice(double price)
        {
            lock (Locker)
            {
                return BidLevelsInternal.TryGetValue(price, out var level)
                    ? level
                    : null;
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
        public override OrderBookLevel FindAskLevelByPrice(double price)
        {
            lock (Locker)
            {
                return AskLevelsInternal.TryGetValue(price, out var level)
                    ? level
                    : null;
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
        protected override bool IsForThis(OrderBookLevelBulk bulk) => bulk.OrderBookType is CryptoOrderBookType.L1 or CryptoOrderBookType.L2;

        /// <inheritdoc />
        protected override void UpdateSnapshot(L2Snapshot snapshot)
        {
            snapshot.Update(BidPrice, AskPrice, BidAmount, AskAmount);

            if (snapshot.Bids.Any())
                UpdateQuotes(snapshot.Bids, BidLevelsInternal.Values.Take(snapshot.Bids.Count).ToList());

            if (snapshot.Asks.Any())
                UpdateQuotes(snapshot.Asks, AskLevelsInternal.Values.Take(snapshot.Asks.Count).ToList());

            static void UpdateQuotes(IEnumerable<CryptoQuote> quotes, IReadOnlyList<OrderBookLevel> levels)
            {
                var index = 0;
                foreach (var quote in quotes)
                {
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

                    index++;
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
            foreach (var level in levels)
            {
                var collection = GetLevelsCollection(level.Side);
                if (collection == null)
                    continue;

                var existing = FindLevelById(level.Id, level.Side);
                if (existing == null)
                {
                    level.AmountDifference = level.Amount ?? 0;
                    level.CountDifference = level.Count ?? 0;
                    level.AmountUpdatedCount = -1;

                    InsertToCollection(collection, level);
                    continue;
                }

                ComputeUpdate(existing, level);

                InsertToCollection(collection, existing);
            }
        }

        void InsertToCollection(IDictionary<double, OrderBookLevel> collection, OrderBookLevel level)
        {
            if (collection == null)
                return;

            if (IsInvalidLevel(level))
            {
                LogDebug($"Received weird level, ignoring. Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                return;
            }

            // ReSharper disable once PossibleInvalidOperationException
            collection[level.Price.Value] = level;
            GetAllCollection(level.Side)[level.Id] = level;
            level.AmountUpdatedCount++;
        }

        /// <inheritdoc />
        protected override void DeleteLevels(IReadOnlyCollection<OrderBookLevel> levels)
        {
            FillCurrentIndex(levels);

            foreach (var level in levels)
            {
                var collection = GetLevelsCollection(level.Side);
                if (collection == null)
                    continue;

                var price = level.Price ?? -1;
                var allLevels = GetAllCollection(level.Side);
                OrderBookLevel existing = null;

                if (collection.ContainsKey(price))
                {
                    existing = collection[price];
                    collection.Remove(price);
                }
                else if (allLevels.ContainsKey(level.Id))
                {
                    existing = allLevels[level.Id];
                    var existingPrice = existing.Price ?? -1;
                    collection.Remove(existingPrice);
                }

                allLevels.Remove(level.Id);

                if (existing != null)
                    ComputeDelete(existing, level);
            }
        }

        /// <inheritdoc />
        protected override OrderBookLevel GetFirstBid() => BidLevelsInternal.FirstOrDefault().Value;

        /// <inheritdoc />
        protected override OrderBookLevel GetFirstAsk() => AskLevelsInternal.FirstOrDefault().Value;

        /// <inheritdoc />
        protected override OrderBookLevel[] ComputeBidLevels()
        {
            lock (Locker)
            {
                return BidLevelsInternal.Values.ToArray();
            }
        }

        /// <inheritdoc />
        protected override OrderBookLevel[] ComputeAskLevels()
        {
            lock (Locker)
            {
                return AskLevelsInternal.Values.ToArray();
            }
        }
    }
}
