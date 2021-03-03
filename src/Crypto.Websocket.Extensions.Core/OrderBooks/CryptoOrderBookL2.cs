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
    /// Cryptocurrency order book optimized for L2 precision (grouped by price).
    /// Process order book data from one source and one target pair.
    /// Only first levels are computed in advance, allocates less memory than CryptoOrderBook counterpart. 
    /// </summary>
    [DebuggerDisplay("CryptoOrderBook [{TargetPair}] bid: {BidPrice} ({_bidLevels.Count}) ask: {AskPrice} ({_askLevels.Count})")]
    public class CryptoOrderBookL2 : ICryptoOrderBook
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();
        private readonly object _locker = new object();

        private readonly IOrderBookSource _source;

        private readonly Subject<OrderBookChangeInfo> _bidAskUpdated = new Subject<OrderBookChangeInfo>();
        private readonly Subject<OrderBookChangeInfo> _topLevelUpdated = new Subject<OrderBookChangeInfo>();
        private readonly Subject<OrderBookChangeInfo> _orderBookUpdated = new Subject<OrderBookChangeInfo>();

        private readonly SortedList<double, OrderBookLevel> _bidLevels = new SortedList<double, OrderBookLevel>(new DescendingComparer());
        private readonly SortedList<double, OrderBookLevel> _askLevels = new SortedList<double, OrderBookLevel>();

        private readonly OrderBookLevelsById _allBidLevels = new OrderBookLevelsById(500);
        private readonly OrderBookLevelsById _allAskLevels = new OrderBookLevelsById(500);

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
        /// <param name="source">Provide level 2 source data</param>
        public CryptoOrderBookL2(string targetPair, IOrderBookSource source)
        {
            CryptoValidations.ValidateInput(targetPair, nameof(targetPair));
            CryptoValidations.ValidateInput(source, nameof(source));

            TargetPairOriginal = targetPair;
            TargetPair = CryptoPairsHelper.Clean(targetPair);
            _source = source;

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
        public CryptoOrderBookType TargetType => CryptoOrderBookType.L2;

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
        public bool DebugLogEnabled { get; set; }

        
        /// <summary>
        /// Whenever snapshot was already handled
        /// </summary>
        public bool IsSnapshotLoaded => _isSnapshotLoaded;

        /// <summary>
        /// All diffs/deltas that come before snapshot will be ignored (default: true)
        /// </summary>
        public bool IgnoreDiffsBeforeSnapshot { get; set; } = true;

        /// <summary>
        /// Compute index (position) per every updated level, performance is slightly reduced (default: false) 
        /// </summary>
        public bool IsIndexComputationEnabled { get; set; }

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
        /// Current ask side of the order book (ordered from lower to higher price)
        /// </summary>
        public OrderBookLevel[] AskLevels => ComputeAskLevels();

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
            return isPriceValid && _source.IsValid();
        }

        /// <summary>
        /// Find bid level by provided price (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindBidLevelByPrice(double price)
        {
            lock (_locker)
            {
                return _bidLevels.TryGetValue(price, out OrderBookLevel level) ? level : null;
            }
        }

        /// <summary>
        /// Find all bid levels for provided price (returns empty when not found)
        /// </summary>
        public OrderBookLevel[] FindBidLevelsByPrice(double price)
        {
            var level = FindBidLevelByPrice(price);
            if (level == null)
                return Array.Empty<OrderBookLevel>();
            return new[] {level};
        }

        /// <summary>
        /// Find ask level by provided price (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindAskLevelByPrice(double price)
        {
            lock (_locker)
            {
                return _askLevels.TryGetValue(price, out OrderBookLevel level) ? level : null;
            }
        }

        /// <summary>
        /// Find all ask levels for provided price (returns empty when not found)
        /// </summary>
        public OrderBookLevel[] FindAskLevelsByPrice(double price)
        {
            var level = FindAskLevelByPrice(price);
            if (level == null)
                return Array.Empty<OrderBookLevel>();
            return new[] {level};
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
            var collection = GetAllCollection(side);
            if (collection.ContainsKey(id))
                return collection[id];
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
            if (bulk == null)
                return;

            var levels = bulk.Levels;
            var levelsForThis = levels
                .Where(x => TargetPair.Equals(x.Pair))
                .ToList();
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

                change = NotifyAboutBookChange(
                    levelsForThis,
                    new[] {bulk},
                    true
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
                .Where(x => x.Levels.Any(y => TargetPair.Equals(y.Pair)))
                .ToArray();
            if (!forThis.Any())
            {
                // snapshot for different pair, ignore
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

                RecomputeAfterChangeAndSetIndexes(allLevels);

                change = NotifyAboutBookChange(
                    allLevels,
                    forThis,
                    false
                );
            }

            _orderBookUpdated.OnNext(change);
            NotifyIfTopLevelChanged(oldBid, oldAsk, oldBidAmount, oldAskAmount, change);
            NotifyIfBidAskChanged(oldBid, oldAsk, change);

            if (sw != null)
            {
                var levels = forThis.SelectMany(x => x.Levels).Count();
                LogDebug($"Diff ({forThis.Length} bulks, {levels} levels) processing took {sw.ElapsedMilliseconds} ms, {sw.ElapsedTicks} ticks");
            }
        }

        private void HandleSnapshot(List<OrderBookLevel> levels)
        {
            _bidLevels.Clear();
            _askLevels.Clear();
            _allBidLevels.Clear();
            _allAskLevels.Clear();

            LogDebug($"Handling snapshot: {levels.Count} levels");
            foreach (var level in levels)
            {
                var price = level.Price;
                if (price == null || price < 0)
                {
                    LogAlways($"Received snapshot level with weird price, ignoring. Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                    continue;
                }

                level.AmountDifference = level.Amount ?? 0;
                level.CountDifference = level.Count ?? 0;

                if (level.Side == CryptoOrderSide.Bid)
                {
                    _bidLevels[price.Value] = level;
                    _allBidLevels[level.Id] = level;
                }


                if (level.Side == CryptoOrderSide.Ask)
                {
                    _askLevels[price.Value] = level;
                    _allAskLevels[level.Id] = level;
                }
            }

            RecomputeAfterChangeAndSetIndexes(levels);

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

        private void UpdateLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                var collection = GetLevelsCollection(level.Side);
                if(collection == null)
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

                level.Amount = level.Amount ?? existing.Amount;
                level.Count = level.Count ?? existing.Count;
                level.Price = level.Price ?? existing.Price;

                InsertToCollection(collection, existing);
            }
        }

        private void InsertToCollection(IDictionary<double, OrderBookLevel> collection, OrderBookLevel level)
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

        private static bool IsInvalidLevel(OrderBookLevel level)
        {
            return string.IsNullOrWhiteSpace(level.Id) ||
                   level.Price == null ||
                   level.Amount == null;
        }

        private void DeleteLevels(OrderBookLevel[] levels)
        {
            FillCurrentIndex(levels);

            foreach (var level in levels)
            {
                var collection = GetLevelsCollection(level.Side);
                if(collection == null)
                    continue;

                var price = level.Price ?? -1;
                var allLevels = GetAllCollection(level.Side);
                OrderBookLevel existing = null;

                if (collection.ContainsKey(price))
                {
                    existing = collection[price];
                    collection.Remove(price);
                }
                else if(allLevels.ContainsKey(level.Id))
                {
                    existing = allLevels[level.Id];
                    var existingPrice = existing.Price ?? -1;
                    collection.Remove(existingPrice);
                }
                
                allLevels.Remove(level.Id);

                if (existing != null)
                {
                    var amountDiff =  -(existing.Amount ?? 0);
                    var countDiff = -(existing.Count ?? 0);

                    level.Amount = level.Amount ?? existing.Amount;
                    level.Count = level.Count ?? existing.Count;
                    level.Price = level.Price ?? existing.Price;

                    level.AmountDifference = amountDiff;
                    existing.AmountDifference = amountDiff;

                    level.CountDifference = countDiff;
                    existing.CountDifference = countDiff;

                    level.AmountDifferenceAggregated += amountDiff;
                    existing.AmountDifferenceAggregated += amountDiff;

                    level.CountDifferenceAggregated += countDiff;
                    existing.CountDifferenceAggregated += countDiff;
                }
            }
        }

        private void RecomputeAfterChange()
        {
            var firstBid = _bidLevels.FirstOrDefault().Value;
            var firstAsk = _askLevels.FirstOrDefault().Value;

            BidPrice = firstBid?.Price ?? 0;
            BidAmount = firstBid?.Amount ?? 0;

            AskPrice = firstAsk?.Price ?? 0;
            AskAmount = firstAsk?.Amount ?? 0;
        }

        private void RecomputeAfterChangeAndSetIndexes(List<OrderBookLevel> levels)
        {
            RecomputeAfterChange();
            FillCurrentIndex(levels);
        }

        private void FillCurrentIndex(List<OrderBookLevel> levels)
        {
            if (!IsIndexComputationEnabled)
                return;

            foreach (var level in levels)
            {
                FillIndex(level);
            }
        }

        private void FillCurrentIndex(OrderBookLevel[] levels)
        {
            if (!IsIndexComputationEnabled)
                return;

            foreach (var level in levels)
            {
                FillIndex(level);
            }
        }

        private void FillIndex(OrderBookLevel level)
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

        private OrderBookLevel[] ComputeBidLevels()
        {
            return _bidLevels.Values.ToArray();
        }

        private OrderBookLevel[] ComputeAskLevels()
        {
            return _askLevels.Values.ToArray();
        }

        private OrderBookLevel[] ComputeAllLevels()
        {
            lock (_locker)
            {
                return _allBidLevels.Concat(_allAskLevels)
                    .Select(x => x.Value)
                    .ToArray();
            }
        }

        private SortedList<double, OrderBookLevel> GetLevelsCollection(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;
            return side == CryptoOrderSide.Bid ? 
                _bidLevels : 
                _askLevels;
        }

        private OrderBookLevelsById GetAllCollection(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined)
                return null;
            return side == CryptoOrderSide.Bid ? 
                _allBidLevels : 
                _allAskLevels;
        }

        private OrderBookChangeInfo NotifyAboutBookChange(List<OrderBookLevel> levels, OrderBookLevelBulk[] sources, bool isSnapshot)
        {
            var quotes = new CryptoQuotes(BidPrice, AskPrice, BidAmount, AskAmount);
            var clonedLevels = DebugEnabled ? levels.Select(x => x.Clone()).ToArray() : new OrderBookLevel[0];
            var lastSource = sources.LastOrDefault();
            var change = new OrderBookChangeInfo(
                TargetPair, 
                TargetPairOriginal,
                quotes, 
                clonedLevels,
                sources,
                isSnapshot
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

        private class DescendingComparer : IComparer<double> {
            public int Compare(double x, double y) {
                return y.CompareTo(x);
            }
        }
    }
}
