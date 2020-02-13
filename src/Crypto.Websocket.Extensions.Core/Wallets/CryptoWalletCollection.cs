using System.Collections.Concurrent;
using Crypto.Websocket.Extensions.Core.Wallets.Models;

namespace Crypto.Websocket.Extensions.Core.Wallets
{
    public class CryptoWalletCollection: ConcurrentDictionary<string, CryptoWallet>
    {
        
    }
}