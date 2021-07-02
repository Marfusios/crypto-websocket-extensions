using System;
using System.Collections.Generic;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.Utils;

namespace Crypto.Websocket.Extensions.Tests.Helpers
{
    public static class OrderBookTestUtils
    {
        public static OrderBookLevel[] GetOrderBookSnapshotMockData(string pair, int count)
        {
            var result = new List<OrderBookLevel>();

            for (int i = 0; i < count; i++)
            {
                var bid = CreateLevel(pair, i, count * 2 + i, CryptoOrderSide.Bid);
                result.Add(bid);
            }

            
            for (int i = count*2; i > count; i--)
            {
                var ask = CreateLevel(pair, i, count * 4 + i, CryptoOrderSide.Ask);
                result.Add(ask);
            }

            return result.ToArray();
        }

        public static OrderBookLevel[] GetOrderBookSnapshotMockDataL3(string pair, int count)
        {
            var result = new List<OrderBookLevel>();

            for (int i = 0; i < count; i++)
            {
                var bid = CreateLevel(pair, i/10, count * 2 + i, CryptoOrderSide.Bid, null, 
                    Guid.NewGuid().ToString("N").Substring(0, 8));
                result.Add(bid);
            }

            
            for (int i = count*2; i > count; i--)
            {
                var ask = CreateLevel(pair, i/10, count * 4 + i, CryptoOrderSide.Ask, null, 
                    Guid.NewGuid().ToString("N").Substring(0, 8));
                result.Add(ask);
            }

            return result.ToArray();
        }

        public static OrderBookLevelBulk GetInsertBulkL2(params OrderBookLevel[] levels)
        {
            return new(OrderBookAction.Insert, levels, CryptoOrderBookType.L2);
        }

        public static OrderBookLevelBulk GetUpdateBulkL2(params OrderBookLevel[] levels)
        {
            return new(OrderBookAction.Update, levels, CryptoOrderBookType.L2);
        }

        public static OrderBookLevelBulk GetDeleteBulkL2(params OrderBookLevel[] levels)
        {
            return new(OrderBookAction.Delete, levels, CryptoOrderBookType.L2);
        }

        public static OrderBookLevelBulk GetInsertBulk(CryptoOrderBookType type, params OrderBookLevel[] levels)
        {
            return new(OrderBookAction.Insert, levels, type);
        }

        public static OrderBookLevelBulk GetUpdateBulk(CryptoOrderBookType type, params OrderBookLevel[] levels)
        {
            return new(OrderBookAction.Update, levels, type);
        }

        public static OrderBookLevelBulk GetDeleteBulk(CryptoOrderBookType type, params OrderBookLevel[] levels)
        {
            return new(OrderBookAction.Delete, levels, type);
        }

        public static OrderBookLevel CreateLevel(string pair, double? price, double? amount, CryptoOrderSide side, int? count = 3, string key = null)
        {
            return new(
                key ?? CreateKey(price,side),
                side,
                price,
                amount,
                count,
                pair == null ? null : CryptoPairsHelper.Clean(pair)
            );
        }

        public static OrderBookLevel CreateLevelById(string pair, double? price, double? amount, CryptoOrderSide side, int? count = 3, string key = null)
        {
            return new OrderBookLevel(
                key ?? CreateKey(price,side),
                side,
                null,
                amount,
                count,
                pair == null ? null : CryptoPairsHelper.Clean(pair)
            );
        }

        public static OrderBookLevel CreateLevel(string pair, double? price, CryptoOrderSide side, string key = null)
        {
            return new(
                key ?? CreateKey(price,side),
                side,
                null,
                null,
                null,
                pair == null ? null : CryptoPairsHelper.Clean(pair)
            );
        }

        public static string CreateKey(double? price, CryptoOrderSide side)
        {
            var sideSafe = side == CryptoOrderSide.Bid ? "bid" : "ask";
            return $"{price}-{sideSafe}";
        }
    }
}