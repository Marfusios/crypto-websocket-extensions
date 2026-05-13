using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// Base class for order books.
    /// </summary>
    public abstract class CryptoOrderBookBase<T> : ICryptoOrderBook
    {
        /// <summary>
        /// Object to use for synchronization.
        /// </summary>
#if NET9_0_OR_GREATER
        protected readonly Lock Locker = new();
#else
        protected readonly object Locker = new();
#endif

        /// <summary>
        /// The source.
        /// </summary>
        protected readonly IOrderBookSource Source;

        /// <summary>
        /// The internal collection of bid levels.
        /// </summary>
        protected readonly SortedList<double, T> BidLevelsInternal = new(new DescendingComparer());

        /// <summary>
        /// The internal collection of ask levels.
        /// </summary>
        protected readonly SortedList<double, T> AskLevelsInternal = new();

        /// <summary>
        /// Subject for streaming events when the top bid or ask price changes.
        /// </summary>
        protected readonly Subject<OrderBookChangeInfo> BidAskUpdated = new();

        /// <summary>
        /// Subject for streaming events when the top bid or ask prices or amounts change.
        /// </summary>
        protected readonly Subject<OrderBookChangeInfo> TopLevelUpdated = new();

        /// <summary>
        /// Subject for streaming events when any change is detected.
        /// </summary>
        protected readonly Subject<OrderBookChangeInfo> OrderBookUpdated = new();

        /// <summary>
        /// Subject for streaming events when the top N bid or ask prices or amounts change.
        /// </summary>
        protected readonly Subject<TopNLevelsChangeInfo> TopNLevelsUpdated = new();

        /// <summary>
        /// All the bid levels (not grouped by price).
        /// </summary>
        protected readonly OrderBookLevelsById AllBidLevels = new(500);

        /// <summary>
        /// All the ask levels (not grouped by price).
        /// </summary>
        protected readonly OrderBookLevelsById AllAskLevels = new(500);

        private bool _isSnapshotLoaded;
        private Timer? _snapshotReloadTimer;
        private TimeSpan _snapshotReloadTimeout = TimeSpan.FromMinutes(1);
        private bool _snapshotReloadEnabled;

        private Timer? _validityCheckTimer;
        private TimeSpan _validityCheckTimeout = TimeSpan.FromSeconds(5);
        private bool _validityCheckEnabled = true;
        private int _validityCheckCounter;

        private IDisposable? _subscriptionDiff;
        private IDisposable? _subscriptionSnapshot;
        private int _notifyForLevelAndAbove;

        private L2Snapshot _previous;
        private L2Snapshot _current;

        /// <summary>
        /// Cryptocurrency order book.
        /// Process order book data from one source per one target pair.
        /// </summary>
        /// <param name="targetPair">Select target pair</param>
        /// <param name="source">Provide source data</param>
        protected CryptoOrderBookBase(string targetPair, IOrderBookSource source)
        {
            CryptoValidations.ValidateInput(targetPair, nameof(targetPair));
            CryptoValidations.ValidateInput(source, nameof(source));

            _previous = new L2Snapshot(this, Array.Empty<CryptoQuote>(), Array.Empty<CryptoQuote>());
            _current = new L2Snapshot(this, Array.Empty<CryptoQuote>(), Array.Empty<CryptoQuote>());

            TargetPairOriginal = targetPair;
            TargetPair = CryptoPairsHelper.Clean(targetPair);
            Source = source;

            // diffs are not supported by the source, we need to calculate metrics from snapshots
            CalculateMetricsFromSnapshots = !Source.DiffsSupported;
        }

        /// <summary>
        /// Subscribes to source streams and starts background threads for
        /// auto snapshot reloading and validity checking.
        /// </summary>
        protected void Initialize()
        {
            Subscribe();
            RestartAutoSnapshotReloading();
            RestartValidityChecking();
        }

        /// <summary>
        /// Dispose background processing.
        /// </summary>
        public void Dispose()
        {
            DeactivateAutoSnapshotReloading();
            DeactivateValidityChecking();
            _subscriptionDiff?.Dispose();
            _subscriptionSnapshot?.Dispose();
            _snapshotReloadTimer?.Dispose();
            _validityCheckTimer?.Dispose();
        }

        /// <inheritdoc />
        public string ExchangeName => Source.ExchangeName;

        /// <inheritdoc />
        public string TargetPair { get; }

        /// <inheritdoc />
        public string TargetPairOriginal { get; }

        /// <inheritdoc />
        public CryptoOrderBookType TargetType { get; protected set; }

        /// <inheritdoc />
        public TimeSpan SnapshotReloadTimeout
        {
            get => _snapshotReloadTimeout;
            set
            {
                _snapshotReloadTimeout = value;
                RestartAutoSnapshotReloading();
            }
        }

        /// <inheritdoc />
        public bool SnapshotReloadEnabled
        {
            get => _snapshotReloadEnabled;
            set
            {
                _snapshotReloadEnabled = value;
                RestartAutoSnapshotReloading();
            }
        }

        /// <inheritdoc />
        public TimeSpan ValidityCheckTimeout
        {
            get => _validityCheckTimeout;
            set
            {
                _validityCheckTimeout = value;
                RestartValidityChecking();
            }
        }

        /// <inheritdoc />
        public int ValidityCheckLimit { get; set; } = 6;

        /// <inheritdoc />
        public bool ValidityCheckEnabled
        {
            get => _validityCheckEnabled;
            set
            {
                _validityCheckEnabled = value;
                RestartValidityChecking();
            }
        }

        /// <inheritdoc />
        public bool DebugEnabled { get; set; } = false;

        /// <inheritdoc />
        public bool DebugLogEnabled { get; set; } = false;

        /// <inheritdoc />
        public bool IsSnapshotLoaded => _isSnapshotLoaded;

        /// <inheritdoc />
        public bool IgnoreDiffsBeforeSnapshot { get; set; } = true;

        /// <inheritdoc />
        public bool IsIndexComputationEnabled { get; set; }

        /// <summary>
        /// If exchange provides only snapshots with no diffs, enable this to calculate metrics
        /// </summary>
        public bool CalculateMetricsFromSnapshots { get; set; }

        /// <inheritdoc />
        public IObservable<IOrderBookChangeInfo> BidAskUpdatedStream => BidAskUpdated.AsObservable();

        /// <inheritdoc />
        public IObservable<IOrderBookChangeInfo> TopLevelUpdatedStream => TopLevelUpdated.AsObservable();

        /// <inheritdoc />
        public IObservable<IOrderBookChangeInfo> OrderBookUpdatedStream => OrderBookUpdated.AsObservable();

        /// <inheritdoc />
        public IObservable<ITopNLevelsChangeInfo> TopNLevelsUpdatedStream => TopNLevelsUpdated.AsObservable();

        /// <inheritdoc />
        public int NotifyForLevelAndAbove
        {
            get => _notifyForLevelAndAbove;
            set
            {
                lock (Locker)
                {
                    _notifyForLevelAndAbove = value;
                    _previous = new L2Snapshot(this, BlankQuotes(), BlankQuotes());
                    _current = new L2Snapshot(this, BlankQuotes(), BlankQuotes());
                }

                IReadOnlyList<CryptoQuote> BlankQuotes()
                {
                    var result = new List<CryptoQuote>(_notifyForLevelAndAbove);
                    for (var index = 0; index < _notifyForLevelAndAbove; index++)
                        result.Add(new CryptoQuote(0, 0));

                    return result;
                }
            }
        }

        /// <inheritdoc />
        public OrderBookLevel[] BidLevels => ComputeBidLevels();

        /// <inheritdoc />
        public OrderBookLevel[] AskLevels => ComputeAskLevels();

        /// <inheritdoc />
        public OrderBookLevel[] Levels => ComputeAllLevels();

        /// <inheritdoc />
        public double BidPrice { get; private set; }

        /// <inheritdoc />
        public double AskPrice { get; private set; }

        /// <inheritdoc />
        public double MidPrice => (AskPrice + BidPrice) / 2;

        /// <inheritdoc />
        public double BidAmount { get; private set; }

        /// <inheritdoc />
        public double AskAmount { get; private set; }

        /// <inheritdoc />
        public bool IsValid()
        {
            var isPriceValid = BidPrice <= AskPrice;
            return isPriceValid && Source.IsValid();
        }

        /// <inheritdoc />
        public abstract OrderBookLevel? FindBidLevelByPrice(double price);

        /// <inheritdoc />
        public abstract OrderBookLevel[] FindBidLevelsByPrice(double price);

        /// <inheritdoc />
        public abstract OrderBookLevel? FindAskLevelByPrice(double price);

        /// <inheritdoc />
        public abstract OrderBookLevel[] FindAskLevelsByPrice(double price);

        /// <inheritdoc />
        public OrderBookLevel? FindBidLevelById(string id) => FindLevelById(id, CryptoOrderSide.Bid);

        /// <inheritdoc />
        public OrderBookLevel? FindAskLevelById(string id) => FindLevelById(id, CryptoOrderSide.Ask);

        /// <inheritdoc />
        public OrderBookLevel? FindLevelById(string id, CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;

            var collection = GetAllCollection(side);
            return collection.GetValueOrDefault(id);
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = Source.OrderBookSnapshotStream.Subscribe(HandleSnapshotSynchronized);
            _subscriptionDiff = Source is OrderBookSourceBase sourceBase
                ? sourceBase.SubscribeOrderBookChanges(HandleDiffSynchronized, HandleDiffsSynchronized)
                : Source.OrderBookStream.Subscribe(HandleDiffsSynchronized);
        }

        private void HandleSnapshotSynchronized(OrderBookLevelBulk? bulk)
        {
            if (bulk == null || !IsForThis(bulk))
                return;

            var levelsForThis = FilterLevelsForTargetPair(bulk.Levels);
            if (levelsForThis.Length <= 0)
            {
                // snapshot for different pair, ignore
                return;
            }

            lock (Locker)
            {
                HandleSnapshot(levelsForThis);

                if (HasNotificationObservers)
                {
                    var change = CreateBookChangeNotification(levelsForThis, bulk, true);
                    NotifyOrderBookChanges(change);
                }
            }
        }

        /// <summary>
        /// Is the bulk for this orderbook?
        /// </summary>
        /// <param name="bulk">The bulk.</param>
        /// <returns>True if the bulk is for this orderbook.</returns>
        protected abstract bool IsForThis(OrderBookLevelBulk? bulk);

        private OrderBookLevel[] FilterLevelsForTargetPair(OrderBookLevel[] levels)
        {
            if (levels.Length == 0)
                return Array.Empty<OrderBookLevel>();

            var count = 0;
            foreach (var level in levels)
            {
                if (TargetPair.Equals(level.Pair, StringComparison.Ordinal))
                    count++;
            }

            if (count == 0)
                return Array.Empty<OrderBookLevel>();

            if (count == levels.Length)
                return levels;

            var result = new OrderBookLevel[count];
            var index = 0;
            foreach (var level in levels)
            {
                if (TargetPair.Equals(level.Pair, StringComparison.Ordinal))
                    result[index++] = level;
            }

            return result;
        }

        private OrderBookLevelBulk[] FilterBulksForThis(OrderBookLevelBulk[] bulks)
        {
            var count = 0;
            foreach (var bulk in bulks)
            {
                if (bulk != null && IsForThis(bulk))
                    count++;
            }

            if (count == 0)
                return Array.Empty<OrderBookLevelBulk>();

            var result = new OrderBookLevelBulk[count];
            var index = 0;
            foreach (var bulk in bulks)
            {
                if (bulk != null && IsForThis(bulk))
                    result[index++] = bulk;
            }

            return result;
        }

        private bool HasNotificationObservers =>
            OrderBookUpdated.HasObservers ||
            BidAskUpdated.HasObservers ||
            TopLevelUpdated.HasObservers ||
            TopNLevelsUpdated.HasObservers;

        private void HandleDiffsSynchronized(OrderBookLevelBulk[] bulks)
        {
            var sw = DebugEnabled ? Stopwatch.StartNew() : null;

            if (bulks.Length == 1)
            {
                HandleSingleDiffSynchronized(bulks[0], bulks, sw);
                return;
            }

            var forThis = FilterBulksForThis(bulks);
            if (forThis.Length <= 0)
            {
                // diffs for different pair, ignore
                return;
            }

            var allLevels = new List<OrderBookLevel>();

            lock (Locker)
            {
                foreach (var bulk in forThis)
                {
                    var levelsForThis = FilterLevelsForTargetPair(bulk.Levels);
                    allLevels.AddRange(levelsForThis);
                    HandleDiff(bulk, levelsForThis);
                }

                if (allLevels.Count > 0)
                {
                    RecomputeAfterChangeAndSetIndexes(allLevels);

                    if (HasNotificationObservers)
                    {
                        var change = CreateBookChangeNotification(allLevels, forThis, false);
                        NotifyOrderBookChanges(change);
                    }
                }

                if (sw != null)
                {
                    var levels = 0;
                    foreach (var bulk in forThis)
                        levels += bulk.Levels.Length;

                    LogDebug($"Diff ({forThis.Length} bulks, {levels} levels) processing took {sw.ElapsedMilliseconds} ms, {sw.ElapsedTicks} ticks");
                }
            }
        }

        private void HandleDiffSynchronized(OrderBookLevelBulk bulk)
        {
            var sw = DebugEnabled ? Stopwatch.StartNew() : null;
            HandleSingleDiffSynchronized(bulk, null, sw);
        }

        private void HandleSingleDiffSynchronized(OrderBookLevelBulk? bulk, IReadOnlyList<OrderBookLevelBulk>? sourceBulks, Stopwatch? sw)
        {
            if (bulk == null || !IsForThis(bulk))
                return;

            var levelsForThis = FilterLevelsForTargetPair(bulk.Levels);
            if (levelsForThis.Length <= 0)
                return;

            lock (Locker)
            {
                HandleDiff(bulk, levelsForThis);
                RecomputeAfterChangeAndSetIndexes(levelsForThis);

                if (HasNotificationObservers)
                {
                    var change = sourceBulks == null
                        ? CreateBookChangeNotification(levelsForThis, bulk, false)
                        : CreateBookChangeNotification(levelsForThis, sourceBulks, false);
                    NotifyOrderBookChanges(change);
                }

                if (sw != null)
                    LogDebug($"Diff (1 bulks, {bulk.Levels.Length} levels) processing took {sw.ElapsedMilliseconds} ms, {sw.ElapsedTicks} ticks");
            }
        }

        private void NotifyOrderBookChanges(TopNLevelsChangeInfo levelsChange)
        {
            var hasOrderBookObservers = OrderBookUpdated.HasObservers;
            var hasBidAskObservers = BidAskUpdated.HasObservers;
            var hasTopLevelObservers = TopLevelUpdated.HasObservers;
            var hasTopNObservers = TopNLevelsUpdated.HasObservers && NotifyForLevelAndAbove > 0;

            if (hasOrderBookObservers)
                OrderBookUpdated.OnNext(levelsChange);

            if (!hasBidAskObservers && !hasTopLevelObservers && !hasTopNObservers)
                return;

            (_previous, _current) = (_current, _previous);
            _current.Update(BidPrice, AskPrice, BidAmount, AskAmount);

            var bidAskChanged = NotifyIfBidAskChanged(levelsChange, hasBidAskObservers);
            var topLevelChanged = NotifyIfTopLevelChanged(bidAskChanged, levelsChange, hasTopLevelObservers);

            if (hasTopNObservers)
                NotifyIfTopNLevelsChanged(topLevelChanged, levelsChange);
        }

        private void HandleSnapshot(OrderBookLevel[] levels)
        {
            LogDebug($"Handling snapshot: {levels.Length} levels");

            if (CalculateMetricsFromSnapshots)
            {
                HandleSnapshotWithMetrics(levels);
            }
            else
            {
                HandleSnapshotWithNoMetrics(levels);
            }

            RecomputeAfterChangeAndSetIndexes(levels);

            _isSnapshotLoaded = true;
        }

        /// <summary>
        /// Update the given snapshot.
        /// </summary>
        /// <param name="snapshot">The snapshot to update.</param>
        protected abstract void UpdateSnapshot(L2Snapshot snapshot);

        /// <summary>
        /// Clears all internal levels state. Called at the beginning of handling a snapshot.
        /// </summary>
        protected abstract void ClearLevels();

        /// <summary>
        /// Handle a bid level for a snapshot.
        /// </summary>
        /// <param name="level">The bid level.</param>
        protected abstract void HandleSnapshotBidLevel(OrderBookLevel level);

        /// <summary>
        /// Handle an ask level for a snapshot.
        /// </summary>
        /// <param name="level">The ask level.</param>
        protected abstract void HandleSnapshotAskLevel(OrderBookLevel level);

        private void HandleDiff(OrderBookLevelBulk bulk, IReadOnlyList<OrderBookLevel> correctLevels)
        {
            if (IgnoreDiffsBeforeSnapshot && !_isSnapshotLoaded)
            {
                LogDebug($"Snapshot not loaded yet, ignoring bulk: {bulk.Action} {correctLevels.Count} levels");
                // snapshot is not loaded yet, ignore data
                return;
            }

            //LogDebug($"Handling diff, bulk {currentBulk}/{totalBulks}: {bulk.Action} {correctLevels.Length} levels");
            switch (bulk.Action)
            {
                case OrderBookAction.Insert:
                    UpdateLevels(correctLevels);
                    break;
                case OrderBookAction.Update:
                    UpdateLevels(correctLevels);
                    break;
                case OrderBookAction.Delete:
                    DeleteLevels(correctLevels);
                    break;
                default:
                    return;
            }
        }

        /// <summary>
        /// Updates internal state levels.
        /// </summary>
        /// <param name="levels">The levels to update.</param>
        protected virtual void UpdateLevels(IReadOnlyList<OrderBookLevel> levels)
        {
            UpdateLevels((IEnumerable<OrderBookLevel>)levels);
        }

        /// <summary>
        /// Updates internal state levels.
        /// </summary>
        /// <param name="levels">The levels to update.</param>
        protected abstract void UpdateLevels(IEnumerable<OrderBookLevel> levels);

        /// <summary>
        /// Calculates differences and copies state between existing and new level.
        /// </summary>
        /// <param name="existing">The existing level.</param>
        /// <param name="level">The new level.</param>
        protected static void CalculateMetricsForUpdatedLevel(OrderBookLevel existing, OrderBookLevel level)
        {
            var amountDiff = (level.Amount ?? existing.Amount ?? 0) - (existing.Amount ?? 0);
            var countDiff = (level.Count ?? existing.Count ?? 0) - (existing.Count ?? 0);

            existing.Price = level.Price ?? existing.Price;
            existing.Amount = level.Amount ?? existing.Amount;
            existing.Count = level.Count ?? existing.Count;
            existing.Pair = level.Pair ?? existing.Pair;

            level.AmountDifference = amountDiff;
            existing.AmountDifference = amountDiff;

            level.CountDifference = countDiff;
            existing.CountDifference = countDiff;

            level.AmountDifferenceAggregated += amountDiff;
            existing.AmountDifferenceAggregated += amountDiff;

            level.CountDifferenceAggregated += countDiff;
            existing.CountDifferenceAggregated += countDiff;

            existing.AmountUpdatedCount += amountDiff != 0 ? 1 : 0;
            level.AmountUpdatedCount = existing.AmountUpdatedCount;

            level.Amount ??= existing.Amount;
            level.Count ??= existing.Count;
            level.Price ??= existing.Price;
        }

        /// <summary>
        /// Decides whether or not a level is valid.
        /// </summary>
        /// <param name="level">The level to check.</param>
        /// <returns>True if the given level is valid.</returns>
        protected static bool IsInvalidLevel(OrderBookLevel level) =>
            string.IsNullOrWhiteSpace(level.Id) ||
            level.Price == null ||
            level.Amount == null;

        /// <summary>
        /// Deletes internal state levels.
        /// </summary>
        /// <param name="levels">The levels to delete.</param>
        protected virtual void DeleteLevels(IReadOnlyList<OrderBookLevel> levels)
        {
            DeleteLevels((IReadOnlyCollection<OrderBookLevel>)levels);
        }

        /// <summary>
        /// Deletes internal state levels.
        /// </summary>
        /// <param name="levels">The levels to delete.</param>
        protected abstract void DeleteLevels(IReadOnlyCollection<OrderBookLevel> levels);

        /// <summary>
        /// Computes differences and copies state between existing and new level.
        /// </summary>
        /// <param name="existing">The existing level.</param>
        /// <param name="level">The new level.</param>
        protected static void CalculateMetricsForDeletedLevel(OrderBookLevel existing, OrderBookLevel level)
        {
            var amountDiff = -(existing.Amount ?? 0);
            var countDiff = -(existing.Count ?? 0);

            level.Amount ??= existing.Amount;
            level.Count ??= existing.Count;
            level.Price ??= existing.Price;

            level.AmountDifference = amountDiff;
            existing.AmountDifference = amountDiff;

            level.CountDifference = countDiff;
            existing.CountDifference = countDiff;

            level.AmountDifferenceAggregated += amountDiff;
            existing.AmountDifferenceAggregated += amountDiff;

            level.CountDifferenceAggregated += countDiff;
            existing.CountDifferenceAggregated += countDiff;
        }

        private void HandleSnapshotWithNoMetrics(OrderBookLevel[] levels)
        {
            ClearLevels();
            foreach (var level in levels)
            {
                var price = level.Price;
                if (price is null or < 0)
                {
                    LogAlways($"Received snapshot level with weird price, ignoring. [{level.Side}] Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                    continue;
                }

                level.AmountDifference = level.Amount ?? 0;
                level.CountDifference = level.Count ?? 0;

                switch (level.Side)
                {
                    case CryptoOrderSide.Bid:
                        HandleSnapshotBidLevel(level);
                        AllBidLevels[level.Id] = level;
                        break;
                    case CryptoOrderSide.Ask:
                        HandleSnapshotAskLevel(level);
                        AllAskLevels[level.Id] = level;
                        break;
                }
            }
        }

        private void HandleSnapshotWithMetrics(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                var price = level.Price;
                if (price is null or < 0)
                {
                    LogAlways($"Received snapshot level with weird price, ignoring. [{level.Side}] Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                    continue;
                }

                var existing = FindLevelById(level.Id, level.Side);
                if (existing == null)
                {
                    level.AmountDifference = level.Amount ?? 0;
                    level.CountDifference = level.Count ?? 0;
                    level.AmountUpdatedCount = 0;
                    continue;
                }

                CalculateMetricsForUpdatedLevel(existing, level);
            }

            ClearLevels();
            foreach (var level in levels)
            {
                var price = level.Price;
                if (price is null or < 0)
                {
                    continue;
                }

                switch (level.Side)
                {
                    case CryptoOrderSide.Bid:
                        HandleSnapshotBidLevel(level);
                        AllBidLevels[level.Id] = level;
                        break;
                    case CryptoOrderSide.Ask:
                        HandleSnapshotAskLevel(level);
                        AllAskLevels[level.Id] = level;
                        break;
                }
            }
        }

        private void RecomputeAfterChange()
        {
            var firstBid = GetFirstBid();
            var firstAsk = GetFirstAsk();

            BidPrice = firstBid?.Price ?? 0;
            BidAmount = firstBid?.Amount ?? 0;

            AskPrice = firstAsk?.Price ?? 0;
            AskAmount = firstAsk?.Amount ?? 0;
        }

        /// <summary>
        /// Gets the first bid.
        /// </summary>
        /// <returns>The first bid.</returns>
        protected abstract OrderBookLevel? GetFirstBid();

        /// <summary>
        /// Gets the first ask.
        /// </summary>
        /// <returns>The first ask.</returns>
        protected abstract OrderBookLevel? GetFirstAsk();

        private void RecomputeAfterChangeAndSetIndexes(IEnumerable<OrderBookLevel> levels)
        {
            RecomputeAfterChange();
            FillCurrentIndex(levels);
        }

        /// <summary>
        /// Fills the current index.
        /// </summary>
        /// <param name="levels">The levels to use.</param>
        protected void FillCurrentIndex(IEnumerable<OrderBookLevel> levels)
        {
            if (!IsIndexComputationEnabled)
                return;

            foreach (var level in levels)
                FillIndex(level);
        }

        private void FillIndex(OrderBookLevel level)
        {
            if (level.Index.HasValue)
                return;

            var price = level.Price;
            if (price == null)
            {
                var all = GetAllCollection(level.Side);
                var existing = all.GetValueOrDefault(level.Id);
                price = existing?.Price;
            }

            if (price == null)
                return;

            var collection = GetLevelsCollection(level.Side);
            if (collection == null)
                return;

            var index = collection.IndexOfKey(price.Value);
            level.Index = index;
        }

        /// <summary>
        /// Gets the levels collection for the specified side.
        /// </summary>
        /// <param name="side">The side.</param>
        /// <returns>The levels for the specified side.</returns>
        protected SortedList<double, T>? GetLevelsCollection(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;

            return side == CryptoOrderSide.Bid
                ? BidLevelsInternal
                : AskLevelsInternal;
        }

        /// <summary>
        /// Calculates the bid levels.
        /// </summary>
        /// <returns>The computed bids.</returns>
        protected abstract OrderBookLevel[] ComputeBidLevels();

        /// <summary>
        /// Calculates the ask levels.
        /// </summary>
        /// <returns>The computed asks.</returns>
        protected abstract OrderBookLevel[] ComputeAskLevels();

        private OrderBookLevel[] ComputeAllLevels()
        {
            lock (Locker)
            {
                var result = new OrderBookLevel[AllBidLevels.Count + AllAskLevels.Count];
                var index = 0;

                foreach (var level in AllBidLevels.Values)
                    result[index++] = level;

                foreach (var level in AllAskLevels.Values)
                    result[index++] = level;

                return result;
            }
        }

        /// <summary>
        /// Get all the levels for the specified side.
        /// </summary>
        /// <param name="side">The side.</param>
        /// <returns>All the levels for the specified side.</returns>
        protected OrderBookLevelsById GetAllCollection(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                throw new InvalidOperationException("Cannot get collection for undefined order side");

            return side == CryptoOrderSide.Bid
                ? AllBidLevels
                : AllAskLevels;
        }

        private TopNLevelsChangeInfo CreateBookChangeNotification(IEnumerable<OrderBookLevel> levels, IReadOnlyList<OrderBookLevelBulk> sources, bool isSnapshot)
        {
            var quotes = new CryptoQuotes(BidPrice, AskPrice, BidAmount, AskAmount);
            var clonedLevels = DebugEnabled ? CloneLevels(levels) : Array.Empty<OrderBookLevel>();
            var lastSource = sources.Count > 0 ? sources[sources.Count - 1] : null;
            var change = new TopNLevelsChangeInfo(TargetPair, TargetPairOriginal, quotes, clonedLevels, sources, isSnapshot)
            {
                ExchangeName = lastSource?.ExchangeName,
                ServerSequence = lastSource?.ServerSequence,
                ServerTimestamp = lastSource?.ServerTimestamp
            };
            return change;
        }

        private TopNLevelsChangeInfo CreateBookChangeNotification(IEnumerable<OrderBookLevel> levels, OrderBookLevelBulk source, bool isSnapshot)
        {
            var quotes = new CryptoQuotes(BidPrice, AskPrice, BidAmount, AskAmount);
            var clonedLevels = DebugEnabled ? CloneLevels(levels) : Array.Empty<OrderBookLevel>();
            var change = new TopNLevelsChangeInfo(TargetPair, TargetPairOriginal, quotes, clonedLevels, source, isSnapshot)
            {
                ExchangeName = source.ExchangeName,
                ServerSequence = source.ServerSequence,
                ServerTimestamp = source.ServerTimestamp
            };
            return change;
        }

        private bool NotifyIfBidAskChanged(OrderBookChangeInfo info, bool notify)
        {
            if (!CryptoMathUtils.IsSame(_previous.Bid, info.Quotes.Bid) ||
                !CryptoMathUtils.IsSame(_previous.Ask, info.Quotes.Ask))
            {
                if (notify)
                    BidAskUpdated.OnNext(info);

                return true;
            }
            return false;
        }

        private bool NotifyIfTopLevelChanged(bool bidAskChanged, OrderBookChangeInfo info, bool notify)
        {
            if (bidAskChanged ||
                !CryptoMathUtils.IsSame(_previous.BidAmount, info.Quotes.BidAmount) ||
                !CryptoMathUtils.IsSame(_previous.AskAmount, info.Quotes.AskAmount))
            {
                if (notify)
                    TopLevelUpdated.OnNext(info);

                return true;
            }
            return false;
        }

        private bool NotifyIfTopNLevelsChanged(bool topLevelChanged, TopNLevelsChangeInfo info)
        {
            UpdateSnapshot(_current);

            if (topLevelChanged ||
                HasChange(_previous.Bids, _current.Bids) ||
                HasChange(_previous.Asks, _current.Asks))
            {
                info.Snapshot = new L2Snapshot(this,
                    CopyValidQuotes(_current.Bids),
                    CopyValidQuotes(_current.Asks));

                TopNLevelsUpdated.OnNext(info);
                return true;
            }
            return false;

            static bool HasChange(IReadOnlyList<CryptoQuote> left, IReadOnlyList<CryptoQuote> right)
            {
                if (left.Count != right.Count)
                    return true;

                for (var index = 0; index < left.Count; index++)
                {
                    var leftQuote = left[index];
                    var rightQuote = right[index];

                    if (!CryptoMathUtils.IsSame(leftQuote.Price, rightQuote.Price) ||
                        !CryptoMathUtils.IsSame(leftQuote.Amount, rightQuote.Amount))
                        return true;
                }
                return false;
            }
        }

        private static OrderBookLevel[] CloneLevels(IEnumerable<OrderBookLevel> levels)
        {
            if (levels is ICollection<OrderBookLevel> collection)
            {
                var result = new OrderBookLevel[collection.Count];
                var index = 0;
                foreach (var level in collection)
                    result[index++] = level.Clone();

                return result;
            }

            var cloned = new List<OrderBookLevel>();
            foreach (var level in levels)
                cloned.Add(level.Clone());

            return cloned.ToArray();
        }

        private static IReadOnlyList<CryptoQuote> CopyValidQuotes(IReadOnlyList<CryptoQuote> quotes)
        {
            var result = new List<CryptoQuote>(quotes.Count);
            foreach (var quote in quotes)
            {
                if (quote.IsValid)
                    result.Add(quote);
            }

            return result;
        }

        private async Task ReloadSnapshotWithCheck()
        {
            if (!Source.LoadSnapshotEnabled)
            {
                // snapshot loading disabled on the source, do nothing
                return;
            }

            await ReloadSnapshot();
        }

        private async Task ReloadSnapshot()
        {
            try
            {
                DeactivateAutoSnapshotReloading();
                DeactivateValidityChecking();
                await Source.LoadSnapshot(TargetPairOriginal, 10000);
            }
            catch (Exception e)
            {
                LogAlways(e, $"Failed to reload snapshot for pair '{TargetPair}', error: {e.Message}");
            }
            finally
            {
                RestartAutoSnapshotReloading();
                RestartValidityChecking();
            }
        }

        private async Task CheckValidityAndReload()
        {
            var isValid = IsValid();
            if (isValid)
            {
                // ob is valid, just reset counter and do nothing
                _validityCheckCounter = 0;
                return;
            }

            _validityCheckCounter++;
            if (_validityCheckCounter < ValidityCheckLimit)
            {
                // invalid state, but still in the check limit interval
                // waiting for confirmation
                return;
            }
            _validityCheckCounter = 0;

            LogAlways($"Order book is in invalid state, bid: {BidPrice}, ask: {AskPrice}, reloading snapshot...");
            await ReloadSnapshot();
        }

        private void RestartAutoSnapshotReloading()
        {
            DeactivateAutoSnapshotReloading();

            if (!_snapshotReloadEnabled)
            {
                // snapshot reloading disabled, do not start timer
                return;
            }

            _snapshotReloadTimer = new Timer(async _ => await ReloadSnapshotWithCheck(), null, SnapshotReloadTimeout, SnapshotReloadTimeout);
        }

        private void DeactivateAutoSnapshotReloading()
        {
            _snapshotReloadTimer?.Dispose();
            _snapshotReloadTimer = null;
        }

        private void RestartValidityChecking()
        {
            DeactivateValidityChecking();

            if (!_validityCheckEnabled)
            {
                // validity checking disabled, do not start timer
                return;
            }

            _validityCheckTimer = new Timer(async _ => await CheckValidityAndReload(), null, ValidityCheckTimeout, ValidityCheckTimeout);
        }

        private void DeactivateValidityChecking()
        {
            _validityCheckTimer?.Dispose();
            _validityCheckTimer = null;
        }

        /// <summary>
        /// Log a message if DebugLogEnabled is true.
        /// </summary>
        /// <param name="msg">The message.</param>
        protected void LogDebug(string msg)
        {
            if (DebugLogEnabled)
                LogAlways(msg);
        }

        /// <summary>
        /// Always log a message.
        /// </summary>
        /// <param name="msg">The message.</param>
        protected void LogAlways(string msg) => Source.Logger.LogDebug("[ORDER BOOK {exchangeName} {targetPair}] {message}", ExchangeName, TargetPair, msg);

        /// <summary>
        /// Always log an exception.
        /// </summary>
        /// <param name="e">The exception.</param>
        /// <param name="msg">The message.</param>
        protected void LogAlways(Exception e, string msg) => Source.Logger.LogDebug(e, "[ORDER BOOK {exchangeName} {targetPair}] {message}", ExchangeName, TargetPair, msg);

        private class DescendingComparer : IComparer<double>
        {
            public int Compare(double x, double y)
            {
                return y.CompareTo(x);
            }
        }
    }
}
