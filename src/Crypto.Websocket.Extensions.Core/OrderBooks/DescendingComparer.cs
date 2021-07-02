using System.Collections.Generic;

namespace Crypto.Websocket.Extensions.Core.OrderBooks
{
    class DescendingComparer : IComparer<double>
    {
        public int Compare(double x, double y)
        {
            return y.CompareTo(x);
        }
    }
}