using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Info about changed order book
    /// </summary>
    [DebuggerDisplay("OrderBookChangeInfo [{PairOriginal}] {Quotes} sources: {Sources.Count}")]
    public class OrderBookChangeInfo : CryptoChangeInfo, IOrderBookChangeInfo
    {
        private readonly OrderBookLevelBulk? _singleSource;
        private IReadOnlyList<OrderBookLevelBulk>? _sources;

        /// <inheritdoc />
        public OrderBookChangeInfo(string pair, string pairOriginal,
            ICryptoQuotes quotes, IReadOnlyList<OrderBookLevel>? levels, IReadOnlyList<OrderBookLevelBulk> sources,
            bool isSnapshot)
        {
            Pair = pair;
            PairOriginal = pairOriginal;
            Quotes = quotes;
            _sources = sources ?? Array.Empty<OrderBookLevelBulk>();
            IsSnapshot = isSnapshot;
            Levels = levels ?? Array.Empty<OrderBookLevel>();
        }

        /// <inheritdoc />
        public OrderBookChangeInfo(string pair, string pairOriginal,
            ICryptoQuotes quotes, IReadOnlyList<OrderBookLevel>? levels, OrderBookLevelBulk source,
            bool isSnapshot)
        {
            Pair = pair;
            PairOriginal = pairOriginal;
            Quotes = quotes;
            _singleSource = source;
            IsSnapshot = isSnapshot;
            Levels = levels ?? Array.Empty<OrderBookLevel>();
        }

        /// <summary>
        /// Target pair for this quotes
        /// </summary>
        public string Pair { get; }

        /// <summary>
        /// Unmodified target pair for this quotes
        /// </summary>
        public string PairOriginal { get; }

        /// <summary>
        /// Current quotes
        /// </summary>
        public ICryptoQuotes Quotes { get; }

        /// <summary>
        /// Order book levels that caused the change.
        /// Streamed only when debug mode is enabled. 
        /// </summary>
        public IReadOnlyList<OrderBookLevel> Levels { get; }

        /// <summary>
        /// Source bulks that caused this update (all levels)
        /// </summary>
        public IReadOnlyList<OrderBookLevelBulk> Sources =>
            _sources ??= new SingleSourceList(_singleSource!);

        /// <summary>
        /// Whenever this order book change update comes from snapshot or diffs
        /// </summary>
        public bool IsSnapshot { get; }

        private sealed class SingleSourceList : IReadOnlyList<OrderBookLevelBulk>
        {
            private readonly OrderBookLevelBulk _source;

            public SingleSourceList(OrderBookLevelBulk source)
            {
                _source = source;
            }

            public int Count => 1;

            public OrderBookLevelBulk this[int index]
            {
                get
                {
                    if (index != 0)
                        throw new ArgumentOutOfRangeException(nameof(index));

                    return _source;
                }
            }

            public IEnumerator<OrderBookLevelBulk> GetEnumerator()
            {
                yield return _source;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
