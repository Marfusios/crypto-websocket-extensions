using System;
using CoinbasePro.Client.Websocket.Client;
using CoinbasePro.Client.Websocket.Models.Users;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Core.Wallets.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Sources;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Wallets.Sources
{
    /// <summary>
    /// Coinbase Pro wallet source
    /// </summary>
    public class CoinbaseProWalletSource : WalletSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private CoinbaseProClient _client;
        private CryptoWallet _lastWallet;
        private IDisposable _subscription;

        /// <inheritdoc />
        public CoinbaseProWalletSource(CoinbaseProClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "coinbaseProC";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(CoinbaseProClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.User.Subscribe(HandleWalletSafe);
        }

        private void HandleWalletSafe(User response)
        {
            try
            {
                HandleWallet(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Bitfinex] Failed to handle wallet info, error: '{e.Message}'");
            }
        }

        private void HandleWallet(User response)
        {
            WalletChangedSubject.OnNext(new[] {ConvertWallet(response)});
        }

        private CryptoWallet ConvertWallet(User response)
        {
            //var currency = response.Currency ?? "XBt";

            var wallet = new CryptoWallet
            {
                Currency = response.Pair,
                Balance = Math.Abs(response.Amount ?? 0),
                //?? _lastWallet?.Balance ?? 0,
                BalanceAvailable = response.RemainingSize ?? 0,
                Type = response.Type.ToString()
            };
            _lastWallet = wallet;
            return wallet;
        }
    }
}