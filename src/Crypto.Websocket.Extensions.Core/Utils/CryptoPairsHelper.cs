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
        public static string Clean(string? pair)
        {
            if (string.IsNullOrWhiteSpace(pair))
                return string.Empty;

            var start = 0;
            var end = pair.Length - 1;
            while (start <= end && char.IsWhiteSpace(pair[start]))
                start++;

            while (end >= start && char.IsWhiteSpace(pair[end]))
                end--;

            if (start > end)
                return string.Empty;

            var span = System.MemoryExtensions.AsSpan(pair, start, end - start + 1);
            var outputLength = 0;
            var needsCopy = start != 0 || end != pair.Length - 1;

            foreach (var c in span)
            {
                if (IsSeparator(c))
                {
                    needsCopy = true;
                    continue;
                }

                outputLength++;
                needsCopy |= ToLowerInvariant(c) != c;
            }

            if (outputLength == 0)
                return string.Empty;

            if (!needsCopy)
                return pair;

            return string.Create(outputLength, (Pair: pair, Start: start, End: end), static (destination, source) =>
            {
                var index = 0;
                for (var sourceIndex = source.Start; sourceIndex <= source.End; sourceIndex++)
                {
                    var c = source.Pair[sourceIndex];
                    if (!IsSeparator(c))
                        destination[index++] = ToLowerInvariant(c);
                }
            });
        }

        /// <summary>
        /// Compare two pairs, clean them before
        /// </summary>
        public static bool AreSame(string? firstPair, string? secondPair)
        {
            var first = Clean(firstPair);
            var second = Clean(secondPair);
            return first.Equals(second, System.StringComparison.Ordinal);
        }

        private static bool IsSeparator(char c) => c is '/' or '-' or '\\';

        private static char ToLowerInvariant(char c)
        {
            if (c is >= 'A' and <= 'Z')
                return (char)(c + ('a' - 'A'));

            return c <= 127
                ? c
                : char.ToLowerInvariant(c);
        }
    }
}
