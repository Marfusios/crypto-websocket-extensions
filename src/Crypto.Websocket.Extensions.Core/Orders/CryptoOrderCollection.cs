using System.Collections.Concurrent;
using Crypto.Websocket.Extensions.Core.Orders.Models;

namespace Crypto.Websocket.Extensions.Core.Orders
{
    /// <summary>
    /// Orders collection (thread-safe)
    /// </summary>
    public class CryptoOrderCollection : ConcurrentDictionary<string, CryptoOrder>
    {
    }
}