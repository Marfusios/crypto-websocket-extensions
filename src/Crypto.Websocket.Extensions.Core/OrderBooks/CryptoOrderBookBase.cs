using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.Logging;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    /// <summary>
    /// Base class for order books.
    /// </summary>
    public abstract class CryptoOrderBookBase<T> : ICryptoOrderBook
    {
        static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        /// <summary>
        /// Object to use for synchronization.
        /// </summary>
        protected readonly object Locker = new();

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
        /// All the bid levels (not grouped by price).
        /// </summary>
        protected readonly OrderBookLevelsById AllBidLevels = new(500);

        /// <summary>
        /// All the ask levels (not grouped by price).
        /// </summary>
        protected readonly OrderBookLevelsById AllAskLevels = new(500);

        bool _isSnapshotLoaded;
        Timer _snapshotReloadTimer;
        TimeSpan _snapshotReloadTimeout = TimeSpan.FromMinutes(1);
        bool _snapshotReloadEnabled;

        Timer _validityCheckTimer;
        TimeSpan _validityCheckTimeout = TimeSpan.FromSeconds(5);
        bool _validityCheckEnabled = true;
        int _validityCheckCounter;

        IDisposable _subscriptionDiff;
        IDisposable _subscriptionSnapshot;

        CryptoQuotes _previous;
        CryptoQuotes _current;

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

            _previous = new CryptoQuotes(BidPrice, AskPrice, BidAmount, AskAmount);
            _current = new CryptoQuotes(BidPrice, AskPrice, BidAmount, AskAmount);

            TargetPairOriginal = targetPair;
            TargetPair = CryptoPairsHelper.Clean(targetPair);
            Source = source;
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

        /// <inheritdoc />
        public IObservable<IOrderBookChangeInfo> BidAskUpdatedStream => BidAskUpdated.AsObservable();

        /// <inheritdoc />
        public IObservable<IOrderBookChangeInfo> TopLevelUpdatedStream => TopLevelUpdated.AsObservable();

        /// <inheritdoc />
        public IObservable<IOrderBookChangeInfo> OrderBookUpdatedStream => OrderBookUpdated.AsObservable();

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
        public abstract OrderBookLevel FindBidLevelByPrice(double price);

        /// <inheritdoc />
        public abstract OrderBookLevel[] FindBidLevelsByPrice(double price);

        /// <inheritdoc />
        public abstract OrderBookLevel FindAskLevelByPrice(double price);

        /// <inheritdoc />
        public abstract OrderBookLevel[] FindAskLevelsByPrice(double price);

        /// <inheritdoc />
        public OrderBookLevel FindBidLevelById(string id) => FindLevelById(id, CryptoOrderSide.Bid);

        /// <inheritdoc />
        public OrderBookLevel FindAskLevelById(string id) => FindLevelById(id, CryptoOrderSide.Ask);

        /// <inheritdoc />
        public OrderBookLevel FindLevelById(string id, CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;

            var collection = GetAllCollection(side);
            return collection.ContainsKey(id)
                ? collection[id]
                : null;
        }

        void Subscribe()
        {
            _subscriptionSnapshot = Source.OrderBookSnapshotStream.Subscribe(HandleSnapshotSynchronized);
            _subscriptionDiff = Source.OrderBookStream.Subscribe(HandleDiffsSynchronized);
        }

        void HandleSnapshotSynchronized(OrderBookLevelBulk bulk)
        {
            if (bulk == null || !IsForThis(bulk))
                return;

            var levelsForThis = bulk.Levels.Where(x => TargetPair.Equals(x.Pair)).ToList();
            if (!levelsForThis.Any())
            {
                // snapshot for different pair, ignore
                return;
            }

            lock (Locker)
            {
                HandleSnapshot(levelsForThis);
                var change = CreateBookChangeNotification(levelsForThis, new[] { bulk }, true);
                NotifyOrderBookChanges(change);
            }
        }

        /// <summary>
        /// Is the bulk for this orderbook?
        /// </summary>
        /// <param name="bulk">The bulk.</param>
        /// <returns>True if the bulk is for this orderbook.</returns>
        protected abstract bool IsForThis(OrderBookLevelBulk bulk);

        void HandleDiffsSynchronized(OrderBookLevelBulk[] bulks)
        {
            var sw = DebugEnabled ? Stopwatch.StartNew() : null;

            var forThis = bulks.Where(x => x != null).Where(IsForThis).ToList();
            if (!forThis.Any())
            {
                // diffs for different pair, ignore
                return;
            }

            var allLevels = new List<OrderBookLevel>();

            lock (Locker)
            {
                foreach (var bulk in forThis)
                {
                    var levelsForThis = bulk.Levels.Where(x => TargetPair.Equals(x.Pair)).ToList();
                    allLevels.AddRange(levelsForThis);
                    HandleDiff(bulk, levelsForThis);
                }

                if (allLevels.Any())
                {
                    RecomputeAfterChangeAndSetIndexes(allLevels);
                    var change = CreateBookChangeNotification(allLevels, forThis, false);
                    NotifyOrderBookChanges(change);
                }

                if (sw != null)
                {
                    var levels = forThis.SelectMany(x => x.Levels).Count();
                    LogDebug($"Diff ({forThis.Count} bulks, {levels} levels) processing took {sw.ElapsedMilliseconds} ms, {sw.ElapsedTicks} ticks");
                }
            }
        }

        void NotifyOrderBookChanges(OrderBookChangeInfo info)
        {
            OrderBookUpdated.OnNext(info);

            (_previous, _current) = (_current, _previous);

            var bidAskChanged = NotifyIfBidAskChanged(info);
            NotifyIfTopLevelChanged(bidAskChanged, info);
            UpdateSnapshot(_current);
        }

        void HandleSnapshot(List<OrderBookLevel> levels)
        {
            ClearLevels();

            LogDebug($"Handling snapshot: {levels.Count} levels");
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

            RecomputeAfterChangeAndSetIndexes(levels);

            _isSnapshotLoaded = true;
        }

        void UpdateSnapshot(CryptoQuotes snapshot)
        {
            snapshot.Bid = BidPrice;
            snapshot.Ask = AskPrice;
            snapshot.BidAmount = BidAmount;
            snapshot.AskAmount = AskAmount;
        }

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

        void HandleDiff(OrderBookLevelBulk bulk, IReadOnlyCollection<OrderBookLevel> correctLevels)
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
        protected abstract void UpdateLevels(IEnumerable<OrderBookLevel> levels);

        /// <summary>
        /// Computes differences and copies state between existing and new level.
        /// </summary>
        /// <param name="existing">The existing level.</param>
        /// <param name="level">The new level.</param>
        protected static void ComputeUpdate(OrderBookLevel existing, OrderBookLevel level)
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
        protected abstract void DeleteLevels(IReadOnlyCollection<OrderBookLevel> levels);

        /// <summary>
        /// Computes differences and copies state between existing and new level.
        /// </summary>
        /// <param name="existing">The existing level.</param>
        /// <param name="level">The new level.</param>
        protected static void ComputeDelete(OrderBookLevel existing, OrderBookLevel level)
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

        void RecomputeAfterChange()
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
        protected abstract OrderBookLevel GetFirstBid();

        /// <summary>
        /// Gets the first ask.
        /// </summary>
        /// <returns>The first ask.</returns>
        protected abstract OrderBookLevel GetFirstAsk();

        void RecomputeAfterChangeAndSetIndexes(IEnumerable<OrderBookLevel> levels)
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

        void FillIndex(OrderBookLevel level)
        {
            if (level.Index.HasValue)
                return;

            var price = level.Price;
            if (price == null)
            {
                var all = GetAllCollection(level.Side);
                var existing = all.ContainsKey(level.Id) ? all[level.Id] : null;
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
        protected SortedList<double, T> GetLevelsCollection(CryptoOrderSide side)
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

        OrderBookLevel[] ComputeAllLevels()
        {
            lock (Locker)
            {
                return AllBidLevels.Concat(AllAskLevels).Select(x => x.Value).ToArray();
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
                return null;

            return side == CryptoOrderSide.Bid
                ? AllBidLevels
                : AllAskLevels;
        }

        OrderBookChangeInfo CreateBookChangeNotification(IEnumerable<OrderBookLevel> levels, IReadOnlyList<OrderBookLevelBulk> sources, bool isSnapshot)
        {
            var quotes = new CryptoQuotes(BidPrice, AskPrice, BidAmount, AskAmount);
            var clonedLevels = DebugEnabled ? levels.Select(x => x.Clone()).ToArray() : Array.Empty<OrderBookLevel>();
            var lastSource = sources.LastOrDefault();
            var change = new OrderBookChangeInfo(TargetPair, TargetPairOriginal, quotes, clonedLevels, sources, isSnapshot)
            {
                ExchangeName = lastSource?.ExchangeName,
                ServerSequence = lastSource?.ServerSequence,
                ServerTimestamp = lastSource?.ServerTimestamp
            };
            return change;
        }

        bool NotifyIfBidAskChanged(OrderBookChangeInfo info)
        {
            if (!CryptoMathUtils.IsSame(_previous.Bid, info.Quotes.Bid) ||
                !CryptoMathUtils.IsSame(_previous.Ask, info.Quotes.Ask))
            {
                BidAskUpdated.OnNext(info);
                return true;
            }
            return false;
        }

        bool NotifyIfTopLevelChanged(bool bidAskChanged, OrderBookChangeInfo info)
        {
            if (bidAskChanged ||
                !CryptoMathUtils.IsSame(_previous.BidAmount, info.Quotes.BidAmount) ||
                !CryptoMathUtils.IsSame(_previous.AskAmount, info.Quotes.AskAmount))
            {
                TopLevelUpdated.OnNext(info);
                return true;
            }
            return false;
        }

        async Task ReloadSnapshotWithCheck()
        {
            if (!Source.LoadSnapshotEnabled)
            {
                // snapshot loading disabled on the source, do nothing
                return;
            }

            await ReloadSnapshot();
        }

        async Task ReloadSnapshot()
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

        async Task CheckValidityAndReload()
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

        void RestartAutoSnapshotReloading()
        {
            DeactivateAutoSnapshotReloading();

            if (!_snapshotReloadEnabled)
            {
                // snapshot reloading disabled, do not start timer
                return;
            }

            _snapshotReloadTimer = new Timer(async _ => await ReloadSnapshotWithCheck(), null, SnapshotReloadTimeout, SnapshotReloadTimeout);
        }

        void DeactivateAutoSnapshotReloading()
        {
            _snapshotReloadTimer?.Dispose();
            _snapshotReloadTimer = null;
        }

        void RestartValidityChecking()
        {
            DeactivateValidityChecking();

            if (!_validityCheckEnabled)
            {
                // validity checking disabled, do not start timer
                return;
            }

            _validityCheckTimer = new Timer(async _ => await CheckValidityAndReload(), null, ValidityCheckTimeout, ValidityCheckTimeout);
        }

        void DeactivateValidityChecking()
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
        protected void LogAlways(string msg) => Log.Debug($"[ORDER BOOK {ExchangeName} {TargetPair}] {msg}");

        /// <summary>
        /// Always log an exception.
        /// </summary>
        /// <param name="e">The exception.</param>
        /// <param name="msg">The message.</param>
        protected void LogAlways(Exception e, string msg) => Log.Debug(e, $"[ORDER BOOK {ExchangeName} {TargetPair}] {msg}");
    }
}
