using System.Collections.Generic;
using Crypto.Websocket.Extensions.Core.Models;
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
        public static OrderBookLevelBulk GetInsertBulk(params OrderBookLevel[] levels)
        {
            return new OrderBookLevelBulk(OrderBookAction.Insert, levels);
        }

        public static OrderBookLevelBulk GetUpdateBulk(params OrderBookLevel[] levels)
        {
            return new OrderBookLevelBulk(OrderBookAction.Update, levels);
        }

        public static OrderBookLevelBulk GetDeleteBulk(params OrderBookLevel[] levels)
        {
            return new OrderBookLevelBulk(OrderBookAction.Delete, levels);
        }

        public static OrderBookLevel CreateLevel(string pair, double price, double? amount, CryptoOrderSide side)
        {
            return new OrderBookLevel(
                CreateKey(price,side),
                side,
                price,
                amount,
                3,
                pair == null ? null : CryptoPairsHelper.Clean(pair)
            );
        }

        public static OrderBookLevel CreateLevel(string pair, double price, CryptoOrderSide side)
        {
            return new OrderBookLevel(
                CreateKey(price,side),
                side,
                null,
                null,
                null,
                pair == null ? null : CryptoPairsHelper.Clean(pair)
            );
        }

        public static string CreateKey(double price, CryptoOrderSide side)
        {
            var sideSafe = side == CryptoOrderSide.Bid ? "bid" : "ask";
            return $"{price}-{sideSafe}";
        }
    }
}