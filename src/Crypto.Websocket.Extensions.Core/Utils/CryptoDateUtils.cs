using System;

namespace Crypto.Websocket.Extensions.Core.Utils
{
    /// <summary>
    /// DateTime utils to support high resolution
    /// </summary>
    public static class CryptoDateUtils
    {
        /// <summary>
        /// Unix base datetime (1.1. 1970)
        /// </summary>
        public static readonly DateTime UnixBase = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Convert from unix seconds into DateTime with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static DateTime ConvertFromUnixSeconds(double timeInSec)
        {
            var unixTimeStampInTicks = (long)(timeInSec * TimeSpan.TicksPerSecond);
            return new DateTime(UnixBase.Ticks + unixTimeStampInTicks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Convert from unix seconds into DateTime with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static DateTime? ConvertFromUnixSeconds(double? timeInSec)
        {
            if (!timeInSec.HasValue)
                return null;
            return ConvertFromUnixSeconds(timeInSec.Value);
        }

        /// <summary>
        /// Convert from unix seconds into DateTime with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static DateTime ConvertFromUnixSeconds(decimal timeInSec)
        {
            var unixTimeStampInTicks = (long)(timeInSec * TimeSpan.TicksPerSecond);
            return new DateTime(UnixBase.Ticks + unixTimeStampInTicks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Convert from unix seconds into DateTime with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static DateTime? ConvertFromUnixSeconds(decimal? timeInSec)
        {
            if (!timeInSec.HasValue)
                return null;
            return ConvertFromUnixSeconds(timeInSec.Value);
        }


        /// <summary>
        /// Convert DateTime into unix seconds with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static double ToUnixSeconds(this DateTime date)
        {
            var unixTimeStampInTicks = (date.ToUniversalTime() - UnixBase).Ticks;
            return (double)unixTimeStampInTicks / TimeSpan.TicksPerSecond;
        }

        /// <summary>
        /// Convert DateTime into unix seconds with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static double? ToUnixSeconds(this DateTime? date)
        {
            if (!date.HasValue)
                return null;
            return ToUnixSeconds(date.Value);
        }

        /// <summary>
        /// Convert DateTime into unix seconds with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static decimal ToUnixSecondsDecimal(this DateTime date)
        {
            var unixTimeStampInTicks = Convert.ToDecimal((date.ToUniversalTime() - UnixBase).Ticks);
            return unixTimeStampInTicks / TimeSpan.TicksPerSecond;
        }

        /// <summary>
        /// Convert DateTime into unix seconds with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static decimal? ToUnixSecondsDecimal(this DateTime? date)
        {
            if (!date.HasValue)
                return null;
            return ToUnixSecondsDecimal(date.Value);
        }

        /// <summary>
        /// Convert DateTime into unix seconds string with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static string ToUnixSecondsString(this DateTime? value)
        {
            return value?.ToUnixSecondsString();
        }

        /// <summary>
        /// Convert DateTime into unix seconds string with high resolution (6 decimal places for milliseconds)
        /// </summary>
        public static string ToUnixSecondsString(this DateTime value)
        {
            var seconds = value.ToUnixSecondsDecimal();
            var str = seconds.ToString("0.000000");
            return str;
        }
    }
}
