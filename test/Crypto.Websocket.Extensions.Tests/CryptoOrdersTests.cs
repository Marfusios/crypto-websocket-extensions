using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Tests.Helpers;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests
{
    public class CryptoOrdersTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData(1)]
        [InlineData(333)]
        [InlineData(99999999)]
        public void GenerateClientId_ShouldBeCorrectlyBasedOnSelectedPrefix(int? orderPrefix)
        {
            var orders = new CryptoOrders(GetSourceMock(), orderPrefix);

            for (int i = 0; i < 10; i++)
            {
                var clientId = orders.GenerateClientId();
                long expected = !orderPrefix.HasValue
                    ? i + 1
                    : (orderPrefix.Value * orders.ClientIdPrefixExponent) + i + 1;
                Assert.Equal(expected, clientId);
            }
        }

        private IOrderSource GetSourceMock()
        {
            return new OrderSourceMock();
        }
    }
}
