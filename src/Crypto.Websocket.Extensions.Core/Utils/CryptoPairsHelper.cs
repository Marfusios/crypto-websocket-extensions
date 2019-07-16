namespace Crypto.Websocket.Extensions.Core.Utils
{
    /// <summary>
    /// Helper class for working with pair identifications
    /// </summary>
    public static class CryptoPairsHelper
    {
        /// <summary>
        /// Clean pair from any unnecessary characters and make lowercase
        /// </summary>
        public static string Clean(string pair)
        {
            return (pair ?? string.Empty)
                .Trim()
                .ToLower()
                .Replace("/", "")
                .Replace("-", "")
                .Replace("\\", "");
        }

        /// <summary>
        /// Compare two pairs, clean them before
        /// </summary>
        public static bool AreSame(string firstPair, string secondPair)
        {
            var first = Clean(firstPair);
            var second = Clean(secondPair);
            return first.Equals(second);
        }
    }
}
