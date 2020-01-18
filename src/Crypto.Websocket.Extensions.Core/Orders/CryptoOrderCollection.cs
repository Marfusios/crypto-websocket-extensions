using System.Collections.Concurrent;

namespace Crypto.Websocket.Extensions.Core.Orders
{
    /// <summary>
    /// Orders collection (thread-safe)
    /// </summary>
    public class CryptoOrderCollection : ConcurrentDictionary<string, CryptoOrder>
    {
    }
}