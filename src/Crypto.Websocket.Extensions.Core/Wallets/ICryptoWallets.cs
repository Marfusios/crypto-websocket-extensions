using System;
using Crypto.Websocket.Extensions.Core.Wallets.Models;

namespace Crypto.Websocket.Extensions.Core.Wallets
{
    public interface ICryptoWallets
    {
        /// <summary>
        /// Wallet was changed stream
        /// </summary>

        IObservable<CryptoWallet> WalletUpdatedStream { get; }

        /// <summary>
        /// Origin exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Target pair for this orders data (other orders will be filtered out)
        /// </summary>
        string TargetCurrency { get; }

        /// <summary>
        /// Originally provided target pair for this orders data
        /// </summary>
        string TargetCurrencyOriginal { get; }

        /// <summary>
        /// Get all wallets matching provided currency
        /// </summary>
        /// 
        CryptoWalletCollectionReadonly GetWallet(string symbol);

        //CryptoWalletCollectionReadonly  GetWallets(string symbol, string exchangeName);

        /// <summary>
        /// Returns all orders (ignore prefix for client id)
        /// </summary>
        CryptoWalletCollectionReadonly GetAllWallets();
    }
}