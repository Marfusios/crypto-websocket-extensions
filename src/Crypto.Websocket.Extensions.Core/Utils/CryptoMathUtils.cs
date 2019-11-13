using System;

namespace Crypto.Websocket.Extensions.Core.Utils
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
            return Math.Abs(first - second) < EqualTolerance;
        }

        /// <summary>
        /// Tolerance used for comparing float numbers
        /// </summary>
        public static double EqualTolerance => 1E-8;
    }
}
