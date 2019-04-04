using System;

namespace Crypto.Websocket.Extensions.Utils
{
    /// <summary>
    /// Math utils
    /// </summary>
    public static class CryptoMathUtils
    {
        /// <summary>
        /// Compare two double numbers correctly
        /// </summary>
        public static bool IsSame(double first, double second)
        {
            return Math.Abs(first - second) < 1E-8;
        }
    }
}
