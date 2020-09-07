using System.Collections.Generic;

namespace Crypto.Websocket.Extensions.Core.OrderBooks.Models
{
    /// <summary>
    /// Order book levels per single price ordered by index
    /// </summary>
    public class OrderBookPriceLevels : SortedDictionary<int, OrderBookLevel>
    {
    }
}
