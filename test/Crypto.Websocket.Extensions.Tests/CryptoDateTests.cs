using System;
using Crypto.Websocket.Extensions.Core.Utils;
using Xunit;

namespace Crypto.Websocket.Extensions.Tests
{
    public class CryptoDateTests
    {
        [Theory]
        [InlineData(1577573034.123456, "1577573034.123456")]
        [InlineData(1577573034.123451, "1577573034.123451")]
        [InlineData(0000000000.123456, "0.123456")]
        [InlineData(0.0, "0.000000")]
        public void UnixTimeConversion_ShouldSupportSixDecimalMilliseconds(double? timeInSec, string result)
        {
            var converted = CryptoDateUtils.ConvertFromUnixSeconds(timeInSec);
            var convertedBack = converted.ToUnixSeconds();
            var convertedString = converted.ToUnixSecondsString();

            Assert.Equal(timeInSec, convertedBack);
            Assert.Equal(result, convertedString);
        }

        [Fact]
        public void UnixTimeConversionDecimal_ShouldSupportSixDecimalMilliseconds()
        {
            TestDecimal(1577573034.123456m, "1577573034.123456");
            TestDecimal(1577573034.123451m, "1577573034.123451");
            TestDecimal(0000000000.123456m, "0.123456");
            TestDecimal(0m, "0.000000");
        }

        private static void TestDecimal(decimal? timeInSec, string result)
        {
            var converted = CryptoDateUtils.ConvertFromUnixSeconds(timeInSec);
            var convertedBack = converted.ToUnixSecondsDecimal();
            var convertedString = converted.ToUnixSecondsString();

            Assert.Equal(timeInSec, convertedBack);
            Assert.Equal(result, convertedString);
        }
    }
}
