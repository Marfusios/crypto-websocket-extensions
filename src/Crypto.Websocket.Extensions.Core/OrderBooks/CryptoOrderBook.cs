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
    /// Cryptocurrency order book.
    /// Process order book data from one source and one target pair.
    /// </summary>
    [DebuggerDisplay(
        "CryptoOrderBook [{TargetPair}] bid: {BidPrice} ({_bidLevels.Count}) ask: {AskPrice} ({_askLevels.Count})")]
    public class CryptoOrderBook : ICryptoOrderBook
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private readonly Dictionary<string, OrderBookLevel> _allLevels = new Dictionary<string, OrderBookLevel>(20000);

        private readonly SortedDictionary<double, OrderBookLevel> _askLevels =
            new SortedDictionary<double, OrderBookLevel>();

        private readonly Subject<OrderBookChangeInfo> _bidAskUpdated = new Subject<OrderBookChangeInfo>();

        private readonly SortedDictionary<double, OrderBookLevel> _bidLevels =
            new SortedDictionary<double, OrderBookLevel>(new DescendingComparer<double>());

        private readonly object _locker = new object();
        private readonly Subject<OrderBookChangeInfo> _orderBookUpdated = new Subject<OrderBookChangeInfo>();

        private readonly IOrderBookLevel2Source _source;
        private readonly Subject<OrderBookChangeInfo> _topLevelUpdated = new Subject<OrderBookChangeInfo>();

        private bool _isSnapshotLoaded;
        private bool _snapshotReloadEnabled;
        private TimeSpan _snapshotReloadTimeout = TimeSpan.FromMinutes(1);
        private Timer _snapshotReloadTimer;

        private IDisposable _subscriptionDiff;
        private IDisposable _subscriptionSnapshot;
        private int _validityCheckCounter;
        private bool _validityCheckEnabled = true;
        private TimeSpan _validityCheckTimeout = TimeSpan.FromSeconds(5);

        private Timer _validityCheckTimer;

        /// <summary>
        /// Cryptocurrency order book.
        /// Process order book data from one source per one target pair.
        /// </summary>
        /// <param name="targetPair">Select target pair</param>
        /// <param name="source">Provide level 2 source data</param>
        public CryptoOrderBook(string targetPair, IOrderBookLevel2Source source)
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
        public OrderBookLevel[] BidLevels { get; private set; } = new OrderBookLevel[0];

        /// <summary>
        /// Current ask side of the order book (ordered from lower to higher price)
        /// </summary>
        public OrderBookLevel[] AskLevels { get; private set; } = new OrderBookLevel[0];

        /// <summary>
        /// All current levels together
        /// </summary>
        public OrderBookLevel[] Levels => BidLevels.Concat(AskLevels).ToArray();

        /// <summary>
        /// Current top level bid price
        /// </summary>
        public double BidPrice => BidLevels.FirstOrDefault()?.Price ?? 0;

        /// <summary>
        /// Current top level ask price
        /// </summary>
        public double AskPrice => AskLevels.FirstOrDefault()?.Price ?? 0;

        /// <summary>
        /// Current mid price
        /// </summary>
        public double MidPrice => (AskPrice + BidPrice) / 2;

        /// <summary>
        /// Current top level bid amount
        /// </summary>
        public double BidAmount => BidLevels.FirstOrDefault()?.Amount ?? 0;

        /// <summary>
        /// Current top level ask price
        /// </summary>
        public double AskAmount => AskLevels.FirstOrDefault()?.Amount ?? 0;

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
                return _bidLevels.TryGetValue(price, out var level) ? level : null;
            }
        }

        /// <summary>
        /// Find ask level by provided price (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindAskLevelByPrice(double price)
        {
            lock (_locker)
            {
                return _askLevels.TryGetValue(price, out var level) ? level : null;
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
            if (side == CryptoOrderSide.Undefined) return null;

            if (_allLevels.ContainsKey(id)) return _allLevels[id];

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
            if (bulk == null) return;

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

            lock (_locker)
            {
                oldBid = BidPrice;
                oldAsk = AskPrice;
                oldBidAmount = BidAmount;
                oldAskAmount = AskAmount;
                HandleSnapshot(levelsForThis);
            }

            NotifyAboutBookChange(
                oldBid, oldAsk,
                oldBidAmount, oldAskAmount,
                levelsForThis,
                new[] {bulk}
            );
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
            }

            NotifyAboutBookChange(
                oldBid, oldAsk,
                oldBidAmount, oldAskAmount,
                allLevels.ToArray(),
                forThis
            );

            if (sw != null)
            {
                var levels = forThis.SelectMany(x => x.Levels).Count();
                LogDebug(
                    $"Diff ({forThis.Length} bulks, {levels} levels) processing took {sw.ElapsedMilliseconds} ms, {sw.ElapsedTicks} ticks");
            }
        }

        private void HandleSnapshot(OrderBookLevel[] levels)
        {
            _bidLevels.Clear();
            _askLevels.Clear();
            _allLevels.Clear();

            //LogAlways($"Handling snapshot: {levels.Length} levels");
            foreach (var level in levels)
            {
                var price = level.Price;
                if (price == null || price < 0)
                {
                    LogAlways(
                        $"Received snapshot level with weird price, ignoring. Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                    continue;
                }

                if (level.Side == CryptoOrderSide.Bid) _bidLevels[price.Value] = level;

                if (level.Side == CryptoOrderSide.Ask) _askLevels[price.Value] = level;

                _allLevels[level.Id] = level;
            }

            RecomputeAfterChange();

            _isSnapshotLoaded = true;
        }

        private void HandleDiff(OrderBookLevelBulk bulk, OrderBookLevel[] correctLevels)
        {
            if (!_isSnapshotLoaded)
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

        private void InsertLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                if (level.Side == CryptoOrderSide.Undefined) continue;

                var collection = GetLevelsCollection(level.Side);
                InsertToCollection(collection, level);
            }
        }

        private void UpdateLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                if (level.Side == CryptoOrderSide.Undefined) continue;

                var collection = GetLevelsCollection(level.Side);

                var existing = FindLevelById(level.Id, level.Side);
                if (existing == null)
                {
                    InsertToCollection(collection, level);
                    continue;
                }

                existing.Price = level.Price ?? existing.Price;
                existing.Amount = level.Amount ?? existing.Amount;
                existing.Count = level.Count ?? existing.Count;
                existing.Pair = level.Pair ?? existing.Pair;
                InsertToCollection(collection, existing);
            }
        }

        private void InsertToCollection(IDictionary<double, OrderBookLevel> collection, OrderBookLevel level)
        {
            if (collection == null) return;

            if (IsInvalidLevel(level))
            {
                LogDebug(
                    $"Received weird level, ignoring. Id: {level.Id}, price: {level.Price}, amount: {level.Amount}");
                return;
            }

            // ReSharper disable once PossibleInvalidOperationException
            collection[level.Price.Value] = level;
            _allLevels[level.Id] = level;
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
                if (level.Side == CryptoOrderSide.Undefined) continue;

                var price = level.Price ?? -1;
                var collection = GetLevelsCollection(level.Side);

                if (collection.ContainsKey(price))
                    collection.Remove(price);
                else if (_allLevels.ContainsKey(level.Id))
                {
                    var existing = _allLevels[level.Id];
                    collection.Remove(existing.Price ?? -1);
                }

                _allLevels.Remove(level.Id);
            }
        }

        private void RecomputeAfterChange()
        {
            BidLevels = ComputeBidLevels();
            AskLevels = ComputeAskLevels();
        }

        private OrderBookLevel[] ComputeBidLevels()
        {
            return _bidLevels.Values.ToArray();
        }

        private OrderBookLevel[] ComputeAskLevels()
        {
            return _askLevels.Values.ToArray();
        }

        private IDictionary<double, OrderBookLevel> GetLevelsCollection(CryptoOrderSide side)
        {
            if (side == CryptoOrderSide.Undefined) return null;

            return side == CryptoOrderSide.Bid ? _bidLevels : _askLevels;
        }

        private void NotifyAboutBookChange(double oldBid, double oldAsk,
            double oldBidAmount, double oldAskAmount,
            OrderBookLevel[] levels, OrderBookLevelBulk[] sources)
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

            _orderBookUpdated.OnNext(change);
            NotifyIfBidAskChanged(oldBid, oldAsk, change);
            NotifyIfTopLevelChanged(oldBid, oldAsk, oldBidAmount, oldAskAmount, change);
        }

        private void NotifyIfBidAskChanged(double oldBid, double oldAsk, OrderBookChangeInfo info)
        {
            if (PriceChanged(oldBid, oldAsk, info)) _bidAskUpdated.OnNext(info);
        }

        private void NotifyIfTopLevelChanged(double oldBid, double oldAsk,
            double oldBidAmount, double oldAskAmount,
            OrderBookChangeInfo info)
        {
            if (PriceChanged(oldBid, oldAsk, info) || AmountChanged(oldBidAmount, oldAskAmount, info))
                _topLevelUpdated.OnNext(info);
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

            var timerMs = (int) SnapshotReloadTimeout.TotalMilliseconds;
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

            var timerMs = (int) ValidityCheckTimeout.TotalMilliseconds;
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
            if (!DebugLogEnabled) return;

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

        private class DescendingComparer<T> : IComparer<T> where T : IComparable<T>
        {
            public int Compare(T x, T y)
            {
                // ReSharper disable once PossibleNullReferenceException
                return y.CompareTo(x);
            }
        }
    }
}