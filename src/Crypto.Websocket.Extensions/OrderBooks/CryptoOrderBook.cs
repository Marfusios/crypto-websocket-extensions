using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using Crypto.Websocket.Extensions.Threading;
using Crypto.Websocket.Extensions.Utils;
using Crypto.Websocket.Extensions.Validations;

namespace Crypto.Websocket.Extensions.OrderBooks
{
    /// <summary>
    /// Cryptocurrency order book.
    /// Process order book data from one source per one target pair. 
    /// </summary>
    [DebuggerDisplay("CryptoOrderBook [{TargetPair}] bid: {BidPrice} ({_bidsBook.Count}) ask: {AskPrice} ({_asksBook.Count})")]
    public class CryptoOrderBook
    {
        private readonly CryptoAsyncLock _locker = new CryptoAsyncLock();

        private readonly IOrderBookLevel2Source _source;

        private readonly Subject<CryptoQuotes> _bidAskUpdated = new Subject<CryptoQuotes>();
        private readonly Subject<CryptoQuotes> _orderBookUpdated = new Subject<CryptoQuotes>();

        private readonly ConcurrentDictionary<string, OrderBookLevel> _bidsBook = new ConcurrentDictionary<string, OrderBookLevel>();
        private readonly ConcurrentDictionary<string, OrderBookLevel> _asksBook = new ConcurrentDictionary<string, OrderBookLevel>();

        private bool _isSnapshotLoaded = false;

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
        }

        /// <summary>
        /// Origin exchange name
        /// </summary>
        private string ExchangeName => _source.ExchangeName;

        /// <summary>
        /// Target pair for this order book data
        /// </summary>
        public string TargetPair { get; }

        /// <summary>
        /// Originally provided target pair for this order book data
        /// </summary>
        public string TargetPairOriginal { get; }

        /// <summary>
        /// Streams data when top level bid or ask price was updated
        /// </summary>
        public IObservable<CryptoQuotes> BidAskUpdatedStream => _bidAskUpdated.AsObservable();

        /// <summary>
        /// Streams data on every order book change (price or amount at any level)
        /// </summary>
        public IObservable<CryptoQuotes> OrderBookUpdatedStream => _orderBookUpdated.AsObservable();

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
        /// Find bid level by provided price (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindBidLevelByPrice(double price)
        {
            return _bidsBook
                .Values
                .FirstOrDefault(x => CryptoMathUtils.IsSame(x.Price ?? 0, price));
        }

        /// <summary>
        /// Find ask level by provided price (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindAskLevelByPrice(double price)
        {
            return _asksBook
                .Values
                .FirstOrDefault(x => CryptoMathUtils.IsSame(x.Price ?? 0, price));
        }

        /// <summary>
        /// Find bid level by provided identification (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindBidLevelById(string id)
        {
            if (_bidsBook.ContainsKey(id))
                return _bidsBook[id];
            return null;
        }

        /// <summary>
        /// Find ask level by provided identification (returns null in case of not found)
        /// </summary>
        public OrderBookLevel FindAskLevelById(string id)
        {
            if (_asksBook.ContainsKey(id))
                return _asksBook[id];
            return null;
        }

        /// <summary>
        /// Find level by provided identification (returns null in case of not found).
        /// You need to specify side.
        /// </summary>
        public OrderBookLevel FindLevelById(string id, CryptoSide side)
        {
            if (side == CryptoSide.Undefined)
                return null;
            var collection = GetLevelsCollection(side);
            if (collection.ContainsKey(id))
                return collection[id];
            return null;
        }

        private void Subscribe()
        {
            _source
                .OrderBookSnapshotStream
                .Subscribe(HandleSnapshotSynchronized);

            _source
                .OrderBookStream
                .Subscribe(HandleDiffSynchronized);
        }

        private void HandleSnapshotSynchronized(OrderBookLevel[] levels)
        {
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

            using (_locker.Lock())
            {
                oldBid = BidPrice;
                oldAsk = AskPrice;
                HandleSnapshot(levelsForThis);
            }

            NotifyAboutBookChange(oldBid, oldAsk);
        }

        private void HandleDiffSynchronized(OrderBookLevelBulk bulk)
        {
            var levelsForThis = bulk.Levels
                .Where(x => TargetPair.Equals(x.Pair))
                .ToArray();
            if (!levelsForThis.Any())
            {
                // snapshot for different pair, ignore
                return;
            }

            double oldBid;
            double oldAsk;

            using (_locker.Lock())
            {
                oldBid = BidPrice;
                oldAsk = AskPrice;
                HandleDiff(bulk, levelsForThis);
            }

            NotifyAboutBookChange(oldBid, oldAsk);
        }

        private void HandleSnapshot(OrderBookLevel[] levels)
        {
            _bidsBook.Clear();
            _asksBook.Clear();

            foreach (var level in levels)
            {
                if(!IsTargetPair(level.Pair))
                    continue;

                if (level.Side == CryptoSide.Bid)
                    _bidsBook[level.Id] = level;

                if (level.Side == CryptoSide.Ask)
                    _asksBook[level.Id] = level;
            }

            RecomputeAfterChange();
            _isSnapshotLoaded = true;
        }

        private void HandleDiff(OrderBookLevelBulk bulk, OrderBookLevel[] correctLevels)
        {
            if (!_isSnapshotLoaded)
            {
                // snapshot is not loaded yet, ignore data
                return;
            }

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

            RecomputeAfterChange();
        }

        private void InsertLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                if(level.Side == CryptoSide.Undefined)
                    continue;

                var collection = GetLevelsCollection(level.Side);
                collection[level.Id] = level;
            }
        }

        private void UpdateLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                if(level.Side == CryptoSide.Undefined)
                    continue;
                
                var collection = GetLevelsCollection(level.Side);

                var existing = FindLevelById(level.Id, level.Side);
                if (existing == null)
                {
                    collection[level.Id] = level;
                    continue;
                }

                var clone = new OrderBookLevel(
                    existing.Id,
                    existing.Side,
                    level.Price ?? existing.Price,
                    level.Amount ?? existing.Amount,
                    level.Count ?? existing.Count,
                    level.Pair ?? existing.Pair
                    );
                collection[level.Id] = clone;
            }
        }

        private void DeleteLevels(OrderBookLevel[] levels)
        {
            foreach (var level in levels)
            {
                if(level.Side == CryptoSide.Undefined)
                    continue;

                var collection = GetLevelsCollection(level.Side);
                collection.TryRemove(level.Id, out OrderBookLevel _);
            }
        }

        private bool IsTargetPair(string pair)
        {
            return TargetPair.Equals(pair);
        }

        private void RecomputeAfterChange()
        {
            BidLevels = ComputeBidLevels();
            AskLevels = ComputeAskLevels();
        }

        private OrderBookLevel[] ComputeBidLevels()
        {
            var levels =  _bidsBook
                .Values
                .OrderByDescending(x => x.Price)
                .ToArray();
            return levels;
        }

        private OrderBookLevel[] ComputeAskLevels()
        {
            var levels =  _asksBook
                .Values
                .OrderBy(x => x.Price)
                .ToArray();
            return levels;
        }

        private ConcurrentDictionary<string, OrderBookLevel> GetLevelsCollection(CryptoSide side)
        {
            if (side == CryptoSide.Undefined)
                return null;
            return side == CryptoSide.Bid ? 
                _bidsBook : 
                _asksBook;
        }

        private void NotifyAboutBookChange(double oldBid, double oldAsk)
        {
            var quotes = new CryptoQuotes(BidPrice, AskPrice, _source.ExchangeName, TargetPair);
            _orderBookUpdated.OnNext(quotes);
            NotifyIfBidAskChanged(oldBid, oldAsk, quotes);
        }

        private void NotifyIfBidAskChanged(double oldBid, double oldAsk, CryptoQuotes quotes)
        {
            if (!CryptoMathUtils.IsSame(oldBid, quotes.Bid) || !CryptoMathUtils.IsSame(oldAsk, quotes.Ask))
            {
                _bidAskUpdated.OnNext(quotes);
            }
        }
    }
}
