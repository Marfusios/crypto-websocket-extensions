using Crypto.Websocket.Extensions.Core.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Crypto.Websocket.Extensions.Core.Threading;
using Crypto.Websocket.Extensions.Core.Wallets.Models;

namespace Crypto.Websocket.Extensions.Core.Wallets.Sources
{
    /// <summary>
    /// Source that provides current wallet info
    /// </summary>
    public abstract class WalletSourceBase : IWalletSource
    {
        private static readonly ILog LogBase = LogProvider.GetCurrentClassLogger();

        /// <summary>
        /// Origin exchange name
        /// </summary>
        public abstract string ExchangeName { get; }

        /// <summary>
        /// Wallet subject
        /// </summary>
        protected readonly Subject<CryptoWallet[]> WalletChangedSubject = new Subject<CryptoWallet[]>();

        /// <summary>
        /// Stream info about wallet changes (balance, transactions, etc)
        /// </summary>
        public virtual IObservable<CryptoWallet[]> WalletChangedStream => WalletChangedSubject.AsObservable();
    }
}