using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    /// Cryptocurrency order book.
    /// Process order book data from one source and one target pair.
    /// Only first levels are computed in advance, allocates more memory. 
    /// </summary>
    [DebuggerDisplay("CryptoOrderBook [{TargetPair} {TargetType}] bid: {BidPrice} ({_bidLevels.Count}) ask: {AskPrice} ({_askLevels.Count})")]
    public class CryptoOrderBook : ICryptoOrderBook
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();
        private readonly object _locker = new object();

        private readonly IOrderBookSource _source;

        private readonly Subject<OrderBookChangeInfo> _bidAskUpdated = new Subject<OrderBookChangeInfo>();
        private readonly Subject<OrderBookChangeInfo> _topLevelUpdated = new Subject<OrderBookChangeInfo>();
        private readonly Subject<OrderBookChangeInfo> _orderBookUpdated = new Subject<OrderBookChangeInfo>();

        private readonly SortedDictionary<double, OrderedDictionary> _bidLevels = new SortedDictionary<double, OrderedDictionary>(new DescendingComparer<double>());
        private readonly SortedDictionary<double, OrderedDictionary> _askLevels = new SortedDictionary<double, OrderedDictionary>();

        private readonly OrderBookLevelsOrderPerPrice _bidLevelOrdering = new OrderBookLevelsOrderPerPrice(1000);
        private readonly OrderBookLevelsOrderPerPrice _askLevelOrdering = new OrderBookLevelsOrderPerPrice(1000);

        private readonly OrderBookLevelsById _allLevels = new OrderBookLevelsById(20000);

        private bool _isSnapshotLoaded;
        private Timer _snapshotReloadTimer;
        private TimeSpan _snapshotReloadTimeout = TimeSpan.FromMinutes(1);
        private bool _snapshotReloadEnabled;

        private Timer _validityCheckTimer;
        private TimeSpan _validityCheckTimeout = TimeSpan.FromSeconds(5);
        private bool _validityCheckEnabled = true;
        private int _validityCheckCounter;

        private IDisposable _subscriptionDiff;
        private IDisposable _subscriptionSnapshot;

        /// <summary>
        /// Cryptocurrency order book.
        /// Process order book data from one source per one target pair. 
        /// </summary>
        /// <param name="targetPair">Select target pair</param>
        /// <param name="source">Provide order book data source</param>
        /// <param name="targetType">Select target precision (default: All - accepts every type of data)</param>
        public CryptoOrderBook(string targetPair, IOrderBookSource source, CryptoOrderBookType targetType = CryptoOrderBookType.All)
        {
            CryptoValidations.ValidateInput(targetPair, nameof(targetPair));
            CryptoValidations.ValidateInput(source, nameof(source));

            TargetPairOriginal = targetPair;
            TargetPair = CryptoPairsHelper.Clean(targetPair);
            _source = source;
            TargetType = targetType;

            Subscribe();
            RestartAutoSnapshotReloading();
            RestartValidityChecking();
        }

        /// <summary>
        /// Dispose background processing
        /// </summary>
        public void Dispose()
        {
            DeactivateAutoSnapshotReloading();
            DeactivateValidityChecking();
            _source.Dispose();
            _subscriptionDiff?.Dispose();
            _subscriptionSnapshot?.Dispose();
        }

        /// <summary>
        /// Origin exchange name
        /// </summary>
        public string ExchangeName => _source.ExchangeName;

        /// <summary>
        /// Target pair for this order book data
        /// </summary>
        public string TargetPair { get; }

        /// <summary>
        /// Originally provided target pair for this order book data
        /// </summary>
        public string TargetPairOriginal { get; }

        /// <summary>
        /// Order book type, which precision it supports
        /// </summary>
        public CryptoOrderBookType TargetType { get; }

        /// <summary>
        /// Time interval for auto snapshot reloading.
        /// Default 1 min. 
        /// </summary>
        public TimeSpan SnapshotReloadTimeout
        {
            get => _snapshotReloadTimeout;
            set
            {
                _snapshotReloadTimeout = value;
                RestartAutoSnapshotReloading();
            }
        }

        /// <summary>
        /// Whenever auto snapshot reloading feature is enabled.
        /// Disabled by default
        /// </summary>
        public bool SnapshotReloadEnabled
        {
            get => _snapshotReloadEnabled;
            set
            {
                _snapshotReloadEnabled = value;
                RestartAutoSnapshotReloading();
            }
        }

        /// <summary>
        /// Time interval for validity checking.
        /// It forces snapshot reloading whenever invalid state. 
        /// Default 5 sec. 
        /// </summary>
        public TimeSpan ValidityCheckTimeout
        {
            get => _validityCheckTimeout;
            set
            {
                _validityCheckTimeout = value;
                RestartValidityChecking();
            }
        }

        /// <summary>
        /// How many times it should check validity before processing snapshot reload.
        /// Default 6 times (which is 6 * 5sec = 30sec).
        /// </summary>
        public int ValidityCheckLimit { get; set; } = 6;

        /// <summary>
        /// Whenever validity checking feature is enabled.
        /// It forces snapshot reloading whenever invalid state. 
        /// Enabled by default
        /// </summary>
        public bool ValidityCheckEnabled
        {
            get => _validityCheckEnabled;
            set
            {
                _validityCheckEnabled = value;
                RestartValidityChecking();
            }
        }

        /// <summary>
        /// Provide more info (on every change) whenever enabled. 
        /// Disabled by default
        /// </summary>
        public bool DebugEnabled { get; set; } = false;

        /// <summary>
        /// Logs more info (state, performance) whenever enabled. 
        /// Disabled by default
        /// </summary>
        public bool DebugLogEnabled { get; set; } = false;

        /// <summary>
        /// Whenever snapshot was already handled
        /// </summary>
        public bool IsSnapshotLoaded => _isSnapshotLoaded;

        /// <summary>
        /// All diffs/deltas that come before snapshot will be ignored (default: true)
        /// </summary>
        public bool IgnoreDiffsBeforeSnapshot { get; set; } = true;

        /// <summary>
        /// Streams data when top level bid or ask price was updated
        /// </summary>
        public IObservable<IOrderBookChangeInfo> BidAskUpdatedStream => _bidAskUpdated.AsObservable();

        /// <summary>
        /// Streams data when top level bid or ask price or amount was updated
        /// </summary>
        public IObservable<IOrderBookChangeInfo> TopLevelUpdatedStream => _topLevelUpdated.AsObservable();

        /// <summary>
        /// Streams data on every order book change (price or amount at any level)
        /// </summary>
        public IObservable<IOrderBookChangeInfo> OrderBookUpdatedStream => _orderBookUpdated.AsObservable();

        /// <summary>
        /// Current bid side of the order book (ordered from higher to lower price)
        /// </summary>
        public OrderBookLevel[] BidLevels => ComputeBidLevels();

        /// <summary>
        /// Current bid side of the order book grouped by price (ordered from higher to lower price)
        /// </summary>
        public IReadOnlyDictionary<double, OrderBookLevel[]> BidLevelsPerPrice => ComputeLevelsPerPrice(_bidLevels);

        /// <summary>
        /// Current ask side of the order book (ordered from lower to higher price)
        /// </summary>
        public OrderBookLevel[] AskLevels => ComputeAskLevels();

        /// <summary>
        /// Current ask side of the order book grouped by price (ordered from lower to higher price)
        /// </summary>
        public IReadOnlyDictionary<double, OrderBookLevel[]> AskLevelsPerPrice => ComputeLevelsPerPrice(_askLevels);

        /// <summary>
        /// All current levels together
        /// </summary>
        public OrderBookLevel[] Levels => ComputeAllLevels();

        /// <summary>
        /// Current top level bid price
        /// </summary>
        public double BidPrice { get; private set; }

        /// <summary>
        /// Current top level ask price
        /// </summary>
        public double AskPrice { get; private set; }

        /// <summary>
        /// Current mid price
        /// </summary>
        public double MidPrice => (AskPrice + BidPrice) / 2;

        /// <summary>
        /// Current top level bid amount
        /// </summary>
        public double BidAmount { get; private set; }

        /// <summary>
        /// Current top level ask price
        /// </summary>
        public double AskAmount { get; private set; }

        /// <summary>
        /// Returns true if order book is in valid state
        /// </summary>
        public bool IsValid()
        {
            var isPriceValid = BidPrice <= AskPrice;
            return isPriceValid &&_source.IsValid();
        }


        /// <summary>
        /// Find bid level for provided price (returns null in case of missing)
        /// </summary>
        public OrderBookLevel FindBidLevelByPrice(double price)
        {
            lock (_locker)
            {
                if (_bidLevels.TryGetValue(price, out OrderedDictionary group))
                    return group.Values.OfType<OrderBookLevel>().FirstOrDefault();
                return null;
            }
        }

        /// <summary>
        /// Find all bid levels for provided price (returns empty when not found)
        /// </summary>
        public OrderBookLevel[] FindBidLevelsByPrice(double price)
        {
            lock (_locker)
            {
                if (_bidLevels.TryGetValue(price, out OrderedDictionary group))
                    return group.Values.OfType<OrderBookLevel>().ToArray();
                return Array.Empty<OrderBookLevel>();
            }
        }


        /// <summary>
        /// Find ask level for provided price (returns null in case of missing)
        /// </summary>
        public OrderBookLevel FindAskLevelByPrice(double price)
        {
            lock (_locker)
            {
                if (_askLevels.TryGetValue(price, out OrderedDictionary group))
                    return group.Values.OfType<OrderBookLevel>().FirstOrDefault();
                return null;
            }
        }

        /// <summary>
        /// Find all ask levels for provided price (returns empty when not found)
        /// </summary>
        public OrderBookLevel[] FindAskLevelsByPrice(double price)
        {
            lock (_locker)
            {
                if (_askLevels.TryGetValue(price, out OrderedDictionary group))
                    return group.Values.OfType<OrderBookLevel>().ToArray();
                return Array.Empty<OrderBookLevel>();
            }
        }

        /// <summary>
        /// Find bid level by provided identification (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindBidLevelById(string id)
        {
            return FindLevelById(id, CryptoOrderSide.Bid);
        }

        /// <summary>
        /// Find ask level by provided identification (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindAskLevelById(string id)
        {
            return FindLevelById(id, CryptoOrderSide.Ask);
        }

        /// <summary>
        /// Find level by provided identification (returns null in case of not found).
        /// You need to specify side.
        /// </summary>
        public OrderBookLevel FindLevelById(string id, CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;
            if (_allLevels.ContainsKey(id))
                return _allLevels[id];
            return null;
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _source
                .OrderBookSnapshotStream
                .Subscribe(HandleSnapshotSynchronized);

            _subscriptionDiff = _source
                .OrderBookStream
                .Subscribe(HandleDiffSynchronized);
        }

        private void HandleSnapshotSynchronized(OrderBookLevelBulk bulk)
        {
            if (bulk == null || !IsCorrectType(bulk.OrderBookType))
                return;

            var levels = bulk.Levels;
            var levelsForThis = levels
                .Where(x => TargetPair.Equals(x.Pair))
                .ToArray();
            if (!levelsForThis.Any())
            {
                // snapshot for different pair, ignore
                return;
            }

            double oldBid;
            double oldAsk;
            double oldBidAmount;
            double oldAskAmount;
            OrderBookChangeInfo change;

            lock (_locker)
            {
                oldBid = BidPrice;
                oldAsk = AskPrice;
                oldBidAmount = BidAmount;
                oldAskAmount = AskAmount;
                HandleSnapshot(levelsForThis);

                change = CreateBookChangeNotification(
                    levelsForThis,
                    new[] {bulk}
                );
            }

            _orderBookUpdated.OnNext(change);
            NotifyIfBidAskChanged(oldBid, oldAsk, change);
            NotifyIfTopLevelChanged(oldBid, oldAsk, oldBidAmount, oldAskAmount, change);
        }

        private void HandleDiffSynchronized(OrderBookLevelBulk[] bulks)
        {
            var sw = DebugEnabled ? Stopwatch.StartNew() : null;

            var forThis = bulks
                .Where(x => x != null)
                .Where(x => IsCorrectType(x.OrderBookType))
                .Where(x => x.Levels.Any(y => TargetPair.Equals(y.Pair)))
                .ToArray();
            if (!forThis.Any())
            {
                // data for different pair, ignore
                return;
            }

            double oldBid;
            double oldAsk;
            double oldBidAmount;
            double oldAskAmount;
            var allLevels = new List<OrderBookLevel>();
            OrderBookChangeInfo change;

            lock (_locker)
            {
                oldBid = BidPrice;
                oldAsk = AskPrice;
                oldBidAmount = BidAmount;
                oldAskAmount = AskAmount;

                foreach (var bulk in forThis)
                {
                    var levelsForThis = bulk.Levels
                        .Where(x => TargetPair.Equals(x.Pair))
                        .ToArray();
                    allLevels.AddRange(levelsForThis);
                    HandleDiff(bulk, levelsForThis);
                }

                RecomputeAfterChange();

                change = CreateBookChangeNotification(
                    allLevels.ToArray(),
                    forThis
                );
            }

            _orderBookUpdated.OnNext(change);
            NotifyIfBidAskChanged(oldBid, oldAsk, change);
            NotifyIfTopLevelChanged(oldBid, oldAsk, oldBidAmount, oldAskAmount, change);

            if (sw != null)
            {
                var levels = forThis.SelectMany(x => x.Levels).Count();
                LogDebug($"Diff ({forThis.Length} bulks, {levels} levels) processing took {sw.ElapsedMilliseconds} ms, {sw.ElapsedTicks} ticks");
            }
        }

        private void HandleSnapshot(OrderBookLevel[] levels)
        {
            _bidLevels.Clear();
            _bidLevelOrdering.Clear();
            _askLevels.Clear();
            _askLevelOrdering.Clear();
            _allLevels.Clear();

            LogDebug($"Handling snapshot: {levels.Length} levels");
            foreach (var level in levels)
            {
                var price = level.Price;
                if (price == null || price < 0)
                {
                    LogAlways($"Received snapshot level with weird price, ignoring. " +
                              $"[{level.Side}] Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                    continue;
                }

                if (level.Side == CryptoOrderSide.Bid)
                    InsertLevelIntoPriceGroup(level, _bidLevels, _bidLevelOrdering);

                if (level.Side == CryptoOrderSide.Ask)
                    InsertLevelIntoPriceGroup(level, _askLevels, _askLevelOrdering);

                _allLevels[level.Id] = level;
            }

            RecomputeAfterChange();

            _isSnapshotLoaded = true;
        }

        private void HandleDiff(OrderBookLevelBulk bulk, OrderBookLevel[] correctLevels)
        {
            if (IgnoreDiffsBeforeSnapshot && !_isSnapshotLoaded)
            {
                LogDebug($"Snapshot not loaded yet, ignoring bulk: {bulk.Action} {correctLevels.Length} levels");
                // snapshot is not loaded yet, ignore data
                return;
            }

            //LogDebug($"Handling diff, bulk {currentBulk}/{totalBulks}: {bulk.Action} {correctLevels.Length} levels");
            switch (bulk.Action)
            {
                case OrderBookAction.Insert:
                    InsertLevels(correctLevels);
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

        private bool IsCorrectType(CryptoOrderBookType bulkType)
        {
            if (TargetType == CryptoOrderBookType.All)
            {
                // handling everything, do not filter out
                return true;
            }

            // handle only same type as selected
            return TargetType == bulkType;
        }

        private void InsertLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                if(level.Side == CryptoOrderSide.Undefined)
                    continue;

                var collection = GetLevelsCollection(level.Side);
                var ordering = GetLevelsOrdering(level.Side);
                InsertToCollection(collection, ordering, level);
            }
        }

        private void UpdateLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                if(level.Side == CryptoOrderSide.Undefined)
                    continue;
                
                var collection = GetLevelsCollection(level.Side);
                var ordering = GetLevelsOrdering(level.Side);

                var existing = FindLevelById(level.Id, level.Side);
                if (existing == null)
                {
                    InsertToCollection(collection, ordering, level);
                    continue;
                }

                var previousPrice = existing.Price;

                existing.Price = level.Price ?? existing.Price;
                existing.Amount = level.Amount ?? existing.Amount;
                existing.Count = level.Count ?? existing.Count;
                existing.Pair = level.Pair ?? existing.Pair;
                InsertToCollection(collection, ordering, existing, previousPrice);
            }
        }

        private void InsertToCollection(IDictionary<double, OrderedDictionary> collection, OrderBookLevelsOrderPerPrice ordering, 
            OrderBookLevel level, double? previousPrice = null)
        {
            if (collection == null)
                return;
            if (IsInvalidLevel(level))
            {
                LogDebug($"Received weird level, ignoring. " +
                         $"[{level.Side}] Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                return;
            }

            _allLevels[level.Id] = level;
            InsertLevelIntoPriceGroup(level, collection, ordering, previousPrice);
        }

        private void InsertLevelIntoPriceGroup(OrderBookLevel level, IDictionary<double, OrderedDictionary> collection,
            OrderBookLevelsOrderPerPrice orderingGroup, double? previousPrice = null)
        {
            // remove from last location if needed
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
            }

            // ReSharper disable once PossibleInvalidOperationException
            var price = level.Price.Value;

            if (!orderingGroup.ContainsKey(price))
                orderingGroup[price] = 0;

            if (!collection.ContainsKey(price))
            {
                collection[price] = new OrderedDictionary(100) {{level.Id, level}};
                return;
            }

            var currentGroup = collection[price];
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

        private static bool IsInvalidLevel(OrderBookLevel level)
        {
            return string.IsNullOrWhiteSpace(level.Id) ||
                   level.Price == null ||
                   level.Price.Value < 0 ||
                   level.Amount == null;
        }

        private void DeleteLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                if(level.Side == CryptoOrderSide.Undefined)
                    continue;

                var price = level.Price ?? -1;
                var id = level.Id;
                var collection = GetLevelsCollection(level.Side);
                var ordering = GetLevelsOrdering(level.Side);

                if(_allLevels.ContainsKey(level.Id))
                {
                    var existing = _allLevels[level.Id];
                    price = existing.Price ?? -1;
                    _allLevels.Remove(level.Id);
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

        private void RecomputeAfterChange()
        {
            var bids = _bidLevels.Values.FirstOrDefault();
            var firstBid = bids?.Count > 0 ? (OrderBookLevel)bids[0] : null;

            var asks = _askLevels.Values.FirstOrDefault();
            var firstAsk = asks?.Count > 0 ? (OrderBookLevel)asks[0] : null;

            BidPrice = firstBid?.Price ?? 0;
            BidAmount = firstBid?.Amount ?? 0;

            AskPrice = firstAsk?.Price ?? 0;
            AskAmount = firstAsk?.Amount ?? 0;
        }

        private OrderBookLevel[] ComputeBidLevels()
        {
            lock (_locker)
            {
                return _bidLevels
                    .Values
                    .SelectMany(x => x.Values.Cast<OrderBookLevel>())
                    .ToArray();
            }
        }

        private OrderBookLevel[] ComputeAskLevels()
        {
            lock (_locker)
            {
                return _askLevels
                    .Values
                    .SelectMany(x => x.Values.Cast<OrderBookLevel>())
                    .ToArray();
            }
        }

        private OrderBookLevel[] ComputeAllLevels()
        {
            lock (_locker)
            {
                return _allLevels
                    .Values
                    .ToArray();
            }
        }

        private IReadOnlyDictionary<double, OrderBookLevel[]> ComputeLevelsPerPrice(SortedDictionary<double, OrderedDictionary> levels)
        {
            lock (_locker)
            {
                return levels
                    .ToDictionary(x => x.Key,
                        y => y.Value.Values.Cast<OrderBookLevel>().ToArray());
            }
        }

        private IDictionary<double, OrderedDictionary> GetLevelsCollection(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;
            return side == CryptoOrderSide.Bid ? 
                _bidLevels : 
                _askLevels;
        }

        private OrderBookLevelsOrderPerPrice GetLevelsOrdering(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;
            return side == CryptoOrderSide.Bid ? 
                _bidLevelOrdering : 
                _askLevelOrdering;
        }

        private OrderBookChangeInfo CreateBookChangeNotification(OrderBookLevel[] levels, OrderBookLevelBulk[] sources)
        {
            var quotes = new CryptoQuotes(BidPrice, AskPrice, BidAmount, AskAmount);
            var clonedLevels = DebugEnabled ? levels.Select(x => x.Clone()).ToArray() : new OrderBookLevel[0];
            var lastSource = sources.LastOrDefault();
            var change = new OrderBookChangeInfo(
                TargetPair, 
                TargetPairOriginal,
                quotes, 
                clonedLevels,
                sources
                )
            {
                ExchangeName = lastSource?.ExchangeName,
                ServerSequence = lastSource?.ServerSequence,
                ServerTimestamp = lastSource?.ServerTimestamp
            };
            return change;
        }

        private void NotifyIfBidAskChanged(double oldBid, double oldAsk, OrderBookChangeInfo info)
        {
            if (PriceChanged(oldBid, oldAsk, info))
            {
                _bidAskUpdated.OnNext(info);
            }
        }

        private void NotifyIfTopLevelChanged(double oldBid, double oldAsk, 
            double oldBidAmount, double oldAskAmount,
            OrderBookChangeInfo info)
        {
            if (PriceChanged(oldBid, oldAsk, info) || AmountChanged(oldBidAmount, oldAskAmount, info))
            {
                _topLevelUpdated.OnNext(info);
            }
        }

        private static bool PriceChanged(double oldBid, double oldAsk, OrderBookChangeInfo info)
        {
            return !CryptoMathUtils.IsSame(oldBid, info.Quotes.Bid) || 
                   !CryptoMathUtils.IsSame(oldAsk, info.Quotes.Ask);
        }

        private static bool AmountChanged(double oldBidAmount, double oldAskAmount, OrderBookChangeInfo info)
        {
            return !CryptoMathUtils.IsSame(oldBidAmount, info.Quotes.BidAmount) || 
                   !CryptoMathUtils.IsSame(oldAskAmount, info.Quotes.AskAmount);
        }

        private async Task ReloadSnapshotWithCheck()
        {
            if (!_source.LoadSnapshotEnabled)
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
                await _source.LoadSnapshot(TargetPairOriginal, 10000);
            }
            catch (Exception e)
            {
                LogAlways(e, $"Failed to reload snapshot for pair '{TargetPair}', " +
                             $"error: {e.Message}");
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

            LogAlways($"Order book is in invalid state, bid: {BidPrice}, ask: {AskPrice}, " +
                         "reloading snapshot...");
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

            var timerMs = (int)SnapshotReloadTimeout.TotalMilliseconds;
            _snapshotReloadTimer = new Timer(async _ => await ReloadSnapshotWithCheck(), 
                null, timerMs, timerMs);
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

            var timerMs = (int)ValidityCheckTimeout.TotalMilliseconds;
            _validityCheckTimer = new Timer(async _ => await CheckValidityAndReload(), 
                null, timerMs, timerMs);
        }

        private void DeactivateValidityChecking()
        {
            _validityCheckTimer?.Dispose();
            _validityCheckTimer = null;
        }

        private void LogDebug(string msg)
        {
            if (!DebugLogEnabled)
                return;
            LogAlways(msg);
        }

        private void LogAlways(string msg)
        {
            Log.Debug($"[ORDER BOOK {ExchangeName} {TargetPair}] {msg}");
        }

        private void LogAlways(Exception e, string msg)
        {
            Log.Debug(e, $"[ORDER BOOK {ExchangeName} {TargetPair}] {msg}");
        }

        private class DescendingComparer<T> : IComparer<T> where T : IComparable<T> {
            public int Compare(T x, T y) {
                // ReSharper disable once PossibleNullReferenceException
                return y.CompareTo(x);
            }
        }
    }
}
