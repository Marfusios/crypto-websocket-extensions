using System;
using System.Collections.Generic;
using System.Linq;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Structures
{
    internal class OrderBookSide
    {
        private readonly Dictionary<double, OrderBookLeaf> _priceIndex = new Dictionary<double, OrderBookLeaf>(500);
        private readonly OrderBookLeafsById _levelIndex = new OrderBookLeafsById(500);

        public OrderBookSide(CryptoOrderSide side)
        {
            Side = side;
        }

        public CryptoOrderSide Side { get; }

        public OrderBookLeaf Root { get; set; }

        public int Count => _levelIndex.Count;

        internal OrderBookLeafsById LevelIndex => _levelIndex;

        public void Add(OrderBookLevel level)
        {
            if (Root == null)
            {
                CreateRoot(level);
                _levelIndex[level.Id] = Root;
                _priceIndex[level.Price ?? 0] = Root;
                return;
            }

            if (_priceIndex.TryGetValue(level.Price ?? 0, out var foundLeaf))
            {
                AddInto(level, foundLeaf);
                return;
            }

            var newLeaf = AddInto(level, Root);
            _priceIndex[level.Price ?? 0] = newLeaf;
            
        }

        public void Remove(OrderBookLevel level)
        {
            if (Root == null)
                return;

            if (!_levelIndex.TryGetValue(level.Id, out var foundLeaf))
            {
                return;
            }

            RemoveLeaf(level, foundLeaf);
        }

        public void UpdateOrAdd(OrderBookLevel level, double? previousPrice = null, double? previousAmount = null)
        {
            if (!_levelIndex.TryGetValue(level.Id, out var foundLeaf))
            {
                Add(level);
                return;
            }

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (previousPrice != level.Price)
            {
                // price changed, we need to fix order
                level.PriceUpdatedCount++;
                RemoveLeaf(level, foundLeaf, previousPrice);
                Add(level);
                return;
            }

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (previousAmount != level.Amount)
                level.AmountUpdatedCount++;

            foundLeaf.Data = level;

            MoveOrderToEndOfQueue(foundLeaf);
        }

        private void MoveOrderToEndOfQueue(OrderBookLeaf foundLeaf)
        {
            var price = foundLeaf.Data.Price ?? 0;
            var isRoot = Root == foundLeaf;
            var isRootSet = false;

            _priceIndex.TryGetValue(price, out var priceHead);
            var isPriceHead = priceHead == foundLeaf;
            var isPriceHeadSet = false;

            while (true)
            {
                var next = foundLeaf.Next;
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (next == null || next.Data.Price != foundLeaf.Data.Price)
                    return;

                if (isRoot && !isRootSet)
                {
                    Root = next;
                    isRootSet = true;
                }

                if (isPriceHead && !isPriceHeadSet)
                {
                    _priceIndex[price] = next;
                    isPriceHeadSet = true;
                }

                next.Previous = foundLeaf.Previous;
                foundLeaf.Previous = next;

                foundLeaf.Next = next.Next;
                next.Next = foundLeaf;
                
            }
        }

        public void Clear()
        {
            _priceIndex.Clear();
            _levelIndex.Clear();
            if (Root == null)
                return;
            ClearFullTree();
            Root = null;
        }

        public OrderBookLevel[] GetAll()
        {
            var result = new OrderBookLevel[_levelIndex.Count];
            var counter = 0;
            TraverseTree(level =>
            {
                result[counter] = level.Data;
                counter++;
            });
            return result;
        }

        public OrderBookLevel GetByPriceFirst(double price)
        {
            if (!_priceIndex.TryGetValue(price, out var foundLeaf))
            {
                return null;
            }

            return foundLeaf.Data;
        }

        public OrderBookLevel[] GetByPrice(double price)
        {
            if (!_priceIndex.TryGetValue(price, out var foundLeaf))
            {
                return null;
            }

            return GetSubtree(foundLeaf);
        }

        public IReadOnlyDictionary<double, OrderBookLevel[]> GetAllGroupedByPrice()
        {
            return _priceIndex.ToDictionary(
                x => x.Key, 
                y => GetSubtree(y.Value));
        }

        private void CreateRoot(OrderBookLevel level)
        {
            Root = new OrderBookLeaf(level);
        }

        private OrderBookLeaf AddInto(OrderBookLevel newLevel, OrderBookLeaf leaf)
        {
            var current = leaf;
            var previous = leaf.Previous;
            var isRoot = Root == current;
            var isNewRoot = true;
            while (current != null && CompareLevels(current.Data, newLevel))
            {
                previous = current;
                current = current.Next;
                isNewRoot = false;
            }

            var newLeaf = new OrderBookLeaf(newLevel)
            {
                Next = current,
                Previous = previous
            };

            if (isRoot && isNewRoot)
                Root = newLeaf;

            if (previous != null)
                previous.Next = newLeaf;
            if (current != null)
                current.Previous = newLeaf;
            _levelIndex[newLevel.Id] = newLeaf;
            return newLeaf;
        }

        private void RemoveLeaf(OrderBookLevel level, OrderBookLeaf foundLeaf, double? previousPrice = null)
        {
            _levelIndex.Remove(level.Id);
            var foundPrice = previousPrice ?? foundLeaf.Data.Price ?? 0;
            var next = foundLeaf.Next;

            if (foundLeaf.Previous != null)
                foundLeaf.Previous.Next = next;
            if (next != null)
            {
                next.Previous = foundLeaf.Previous;

                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (next.Data.Price != foundPrice)
                {
                    // no more leafs on this price level, clear
                    _priceIndex.Remove(foundPrice);
                }
                else
                {
                    _priceIndex[foundPrice] = GetFirstOnSamePrice(next);
                }
            }
            else
            {
                var firstSameOnPrice = GetFirstOnSamePrice(foundLeaf);
                if (firstSameOnPrice == foundLeaf)
                    _priceIndex.Remove(foundPrice);
                else
                    _priceIndex[foundPrice] = firstSameOnPrice;
            }

            foundLeaf.Next = null;
            foundLeaf.Previous = null;
            foundLeaf.Data = null;

            if (Root == foundLeaf)
                Root = next;
        }

        private OrderBookLeaf GetFirstOnSamePrice(OrderBookLeaf leaf)
        {
            var current = leaf;
            while (true)
            {
                var prev = current.Previous;
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (prev == null || prev.Data.Price != current.Data.Price)
                    return current;
                current = prev;
            }
        }

        private void TraverseTree(Action<OrderBookLeaf> action)
        {
            var current = Root;
            if (current == null)
                return;

            while (true)
            {
                action(current);
                if (current.Next == null)
                    return;
                current = current.Next;
            }
        }

        private void ClearFullTree()
        {
            var current = Root;
            if (current == null)
                return;

            while (true)
            {
                var previous = current;
                previous.Previous = null;

                if (current.Next == null)
                    return;

                current = current.Next;
                previous.Next = null;
            }
        }

        private OrderBookLevel[] GetSubtree(OrderBookLeaf leaf)
        {
            var result = new List<OrderBookLevel>();
            var current = leaf;
            result.Add(current.Data);

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            while (current?.Next?.Data != null && current.Next?.Data?.Price == current.Data.Price)
            {
                current = current.Next;
                result.Add(current.Data);
            }

            return result.ToArray();
        }

        private bool CompareLevels(OrderBookLevel existing, OrderBookLevel newOne)
        {
            return Side == CryptoOrderSide.Bid ?
                existing.Price >= newOne.Price :
                existing.Price <= newOne.Price;
        }
    }
}
