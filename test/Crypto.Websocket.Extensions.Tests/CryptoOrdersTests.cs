using System;
using System.Collections.Generic;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Tests.Helpers;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests
{
    public class CryptoOrdersTests
    {
        private readonly Random _random = new Random();

        [Theory]
        [InlineData(null)]
        [InlineData(1)]
        [InlineData(333)]
        [InlineData(99999999)]
        public void GenerateClientId_ShouldBeCorrectlyBasedOnSelectedPrefix(int? orderPrefix)
        {
            ICryptoOrders orders = new CryptoOrders(GetSourceMock(), orderPrefix);

            for (int i = 0; i < 10; i++)
            {
                var clientId = orders.GenerateClientId();
                long expected = !orderPrefix.HasValue
                    ? i + 1
                    : (orderPrefix.Value * orders.ClientIdPrefixExponent) + i + 1;
                Assert.Equal(expected, clientId);
            }
        }

        [Theory]
        [InlineData(null, 20)]
        [InlineData(333, 10)]
        [InlineData(5, 10)]
        [InlineData(99999999, 10)]
        [InlineData(0, 0)]
        public void Prefix_ShouldFilterAsExpected(int? prefix, int expectedCount)
        {
            var source = GetSourceMock();
            ICryptoOrders orders = new CryptoOrders(source, prefix);

            var updatedAll = new List<CryptoOrder>();
            var updatedOurs = new List<CryptoOrder>();

            orders.OrderChangedStream.Subscribe(x => updatedAll.Add(x));
            orders.OurOrderChangedStream.Subscribe(x => updatedOurs.Add(x));

            for (int i = 0; i < 10; i++)
            {
                var clientId = orders.GenerateClientId().ToString();
                var side = i % 2 == 0 ? CryptoOrderSide.Bid : CryptoOrderSide.Ask;

                source.StreamOrder(GetOrderMock(clientId, "btcusd", side));
                source.StreamOrder(GetOrderMock(null, "btcusd", side));
            }

            var currentAll = orders.GetAllOrders();
            var currentOurs = orders.GetOrders();

            Assert.Equal(expectedCount, updatedOurs.Count);
            Assert.Equal(expectedCount, currentOurs.Count);

            Assert.Equal(20, updatedAll.Count);
            Assert.Equal(20, currentAll.Count);

            Assert.Equal(prefix * orders.ClientIdPrefixExponent, orders.ClientIdPrefix);
            Assert.Equal(prefix?.ToString() ?? string.Empty, orders.ClientIdPrefixString);
        }

        [Theory]
        [InlineData(null, 20)]
        [InlineData(333, 10)]
        [InlineData(5, 10)]
        [InlineData(99999999, 10)]
        [InlineData(0, 0)]
        public void Prefix_WithExtendedClientId_ShouldFilterAsExpected(int? prefix, int expectedCount)
        {
            var source = GetSourceMock();
            ICryptoOrders orders = new CryptoOrders(source, prefix);

            var updatedAll = new List<CryptoOrder>();
            var updatedOurs = new List<CryptoOrder>();

            orders.OrderChangedStream.Subscribe(x => updatedAll.Add(x));
            orders.OurOrderChangedStream.Subscribe(x => updatedOurs.Add(x));

            for (int i = 0; i < 10; i++)
            {
                var clientId = orders.GenerateClientId().ToString() + "::test_" + i;
                var side = i % 2 == 0 ? CryptoOrderSide.Bid : CryptoOrderSide.Ask;

                source.StreamOrder(GetOrderMock(clientId, "btcusd", side));
                source.StreamOrder(GetOrderMock(null, "btcusd", side));
            }

            var currentAll = orders.GetAllOrders();
            var currentOurs = orders.GetOrders();
            var currentActive = orders.GetActiveOrders();

            Assert.Equal(expectedCount, updatedOurs.Count);
            Assert.Equal(expectedCount, currentOurs.Count);

            Assert.Equal(20, updatedAll.Count);
            Assert.Equal(20, currentAll.Count);
            Assert.Empty(currentActive);

            Assert.Equal(prefix * orders.ClientIdPrefixExponent, orders.ClientIdPrefix);
            Assert.Equal(prefix?.ToString() ?? string.Empty, orders.ClientIdPrefixString);
        }

        [Theory]
        [InlineData(null, 20)]
        [InlineData("  ", 20)]
        [InlineData("", 20)]
        [InlineData("btcusd", 10)]
        [InlineData("btcusd ", 10)]
        [InlineData("BTCUSD", 10)]
        [InlineData(" BTCUSD ", 10)]
        [InlineData("btc/usd", 10)]
        public void TargetPair_ShouldFilterAsExpected(string pair, int expectedCount)
        {
            var source = GetSourceMock();
            ICryptoOrders orders = new CryptoOrders(source, null, pair);

            var updatedAll = new List<CryptoOrder>();
            var updatedOurs = new List<CryptoOrder>();

            orders.OrderChangedStream.Subscribe(x => updatedAll.Add(x));
            orders.OurOrderChangedStream.Subscribe(x => updatedOurs.Add(x));
            var currentActive = orders.GetActiveOrders();

            for (int i = 0; i < 10; i++)
            {
                var clientId1 = orders.GenerateClientId().ToString();
                var clientId2 = orders.GenerateClientId().ToString();
                var side = i % 2 == 0 ? CryptoOrderSide.Bid : CryptoOrderSide.Ask;

                source.StreamOrder(GetOrderMock(clientId1, "btcusd", side));
                source.StreamOrder(GetOrderMock(clientId2, "neobtc", side));
            }

            var currentAll = orders.GetAllOrders();
            var currentOurs = orders.GetOrders();

            Assert.Equal(expectedCount, updatedOurs.Count);
            Assert.Equal(expectedCount, currentOurs.Count);

            Assert.Equal(expectedCount, updatedAll.Count);
            Assert.Equal(expectedCount, currentAll.Count);
            Assert.Empty(currentActive);

            Assert.Equal(orders.TargetPairOriginal, pair);
            Assert.Equal(orders.TargetPair, CryptoPairsHelper.Clean(pair));
        }

        private CryptoOrder GetOrderMock(string clientId, string pair, CryptoOrderSide side)
        {
            return CryptoOrder.Mock(
                clientId,
                pair,
                side,
                _random.Next(10, 10000),
                _random.Next(1, 1000),
                CryptoOrderType.Market,
                DateTime.UtcNow
                );
        }

        private OrderSourceMock GetSourceMock()
        {
            return new OrderSourceMock();
        }
    }
}
