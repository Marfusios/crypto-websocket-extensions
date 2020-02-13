using System.Collections.Generic;
using System.Collections.ObjectModel;
using Crypto.Websocket.Extensions.Core.Wallets.Models;

namespace Crypto.Websocket.Extensions.Core.Wallets
{
    public class CryptoWalletCollectionReadonly: ReadOnlyDictionary<string, CryptoWallet>
    {
        /// <inheritdoc />
        public CryptoWalletCollectionReadonly(IDictionary<string, CryptoWallet> dictionary) : base(dictionary)
        {
        }
    }
}