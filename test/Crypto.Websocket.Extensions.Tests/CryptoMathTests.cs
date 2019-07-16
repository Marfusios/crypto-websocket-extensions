using Crypto.Websocket.Extensions.Core.Utils;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests
{
    public class CryptoMathTests
    {
        [Theory]
        [InlineData(10.0, 10.0)]
        [InlineData(0.0, 0.0)]
        [InlineData(-33.0, -33.0)]
        [InlineData(978.654321, 978.654321)]
        [InlineData(978.654321001, 978.654321)]
        [InlineData(978.654321001, 978.654321003)]
        [InlineData(111222333.000000001, 111222333.000000003)]
        public void IsSame_SameValues_ShouldReturnTrue(double first, double second)
        {
            Assert.True(CryptoMathUtils.IsSame(first, second));
        }

        [Theory]
        [InlineData(10.0, 20.0)]
        [InlineData(0.0, 1.0)]
        [InlineData(-33.0, 33.0)]
        [InlineData(-111222333.654321, -778.654321)]
        [InlineData(978.65432101, 978.654321)]
        [InlineData(978.65432101, 978.65432103)]
        [InlineData(111222333.00000001, 111222333.00000003)]
        [InlineData(5.00000001, 5.00000003)]
        public void IsSame_DifferentValues_ShouldReturnFalse(double first, double second)
        {
            Assert.False(CryptoMathUtils.IsSame(first, second));
        }
    }
}
