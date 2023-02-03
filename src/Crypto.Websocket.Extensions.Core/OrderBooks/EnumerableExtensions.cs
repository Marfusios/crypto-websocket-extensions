using System.Collections.Generic;
using System.Linq;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    internal static class EnumerableExtensions
    {
        public static List<T> ToList<T>(this IEnumerable<T> items, int length)
        {
            var list = new List<T>(length);
            list.AddRange(items.Take(length));
            return list;
        }
    }
}
