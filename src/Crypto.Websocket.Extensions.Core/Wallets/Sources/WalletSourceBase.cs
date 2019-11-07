using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Crypto.Websocket.Extensions.Core.Wallets.Models;

namespace Crypto.Websocket.Extensions.Core.Wallets.Sources
{
    /// <summary>
    /// Source that provides current wallet info 
    /// </summary>
    public abstract class WalletSourceBase : IWalletSource
    {
        protected readonly Subject<CryptoWallet[]> WalletChangedSubject = new Subject<CryptoWallet[]>();


        /// <summary>
        /// Origin exchange name
        /// </summary>
        public abstract string ExchangeName { get; }

        /// <summary>
        /// Stream info about wallet changes (balance, transactions, etc)
        /// </summary>
        public virtual IObservable<CryptoWallet[]> WalletChangedStream => WalletChangedSubject.AsObservable();
    }
}
