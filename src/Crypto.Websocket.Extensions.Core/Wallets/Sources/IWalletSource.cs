using System;
using Crypto.Websocket.Extensions.Core.Wallets.Models;

namespace Crypto.Websocket.Extensions.Core.Wallets.Sources
{
    /// <summary>
    /// Source that provides current wallet info
    /// </summary>
    public interface IWalletSource
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Stream info about wallet changes (balance, transactions, etc)
        /// </summary>
        IObservable<CryptoWallet[]> WalletChangedStream { get; }
    }
}