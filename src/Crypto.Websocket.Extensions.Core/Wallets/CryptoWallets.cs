using System;
using System.Linq;
using System.Reactive.Subjects;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Sources;

namespace Crypto.Websocket.Extensions.Core.Wallets
{
    public class CryptoWallets : ICryptoWallets
    {
        private readonly Subject<CryptoWallet> _walletChanged = new Subject<CryptoWallet>();
        private readonly CryptoWalletCollection _currencyToWallet = new CryptoWalletCollection();
        private readonly IWalletSource _source;

        public IObservable<CryptoWallet> WalletUpdatedStream { get; }

        /// <summary>
        ///     Origin exchange name
        /// </summary>
        public string ExchangeName => _source.ExchangeName;

        /// <summary>
        ///     Target pair for this orders data (other orders will be filtered out)
        /// </summary>
        public string TargetCurrency { get; private set; }

        /// <summary>
        ///     Originally provided target pair for this orders data
        /// </summary>
        public string TargetCurrencyOriginal { get; private set; }

        public CryptoWallets(IWalletSource source, string targetCurrency = null)
        {
            CryptoValidations.ValidateInput(targetCurrency, nameof(targetCurrency));
            CryptoValidations.ValidateInput(source, nameof(source));

            _source = source;
            TargetCurrencyOriginal = targetCurrency;
            TargetCurrency = CryptoPairsHelper.Clean(targetCurrency);

            Subscribe();
        }


        public CryptoWalletCollectionReadonly GetAllWallets()
        {
            var wallets = _currencyToWallet
                .ToDictionary(x => x.Key, y => y.Value);
            return new CryptoWalletCollectionReadonly(wallets);
        }

        public CryptoWalletCollectionReadonly GetWallet(string currency)
        {
            var wallets = _currencyToWallet
                .Where(x => x.Value.Currency == currency)
                .ToDictionary(x => x.Key, y => y.Value);
            return new CryptoWalletCollectionReadonly(wallets);
        }

        private void Subscribe()
        {
            _source.WalletChangedStream.Subscribe(OnWalletsUpdated);
        }

        private void OnWalletsUpdated(CryptoWallet[] wallets)
        {
            if (wallets == null)
                return;

            foreach (var wallet in wallets) OnWalletUpdated(wallet);
        }

        private void OnWalletUpdated(CryptoWallet wallet)
        {
            if (wallet == null)
                return;

            HandleWalletUpdated(wallet);
        }

        private void HandleWalletUpdated(CryptoWallet wallet)
        {
            _walletChanged.OnNext(wallet);
        }
    }
}