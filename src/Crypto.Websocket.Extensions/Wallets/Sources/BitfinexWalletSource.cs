using System;
using System.Threading.Tasks;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Responses.Wallets;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Core.Wallets.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Sources;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Wallets.Sources
{
    /// <summary>
    /// Bitfinex wallet source
    /// </summary>
    public class BitfinexWalletSource : WalletSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private BitfinexWebsocketClient _client;
        private CryptoWallet _lastWallet;
        private IDisposable _subscription;
        private IDisposable _subscriptionSnapshot;

        /// <inheritdoc />
        public BitfinexWalletSource(BitfinexWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitfinex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitfinexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.WalletsStream.Subscribe(HandleWalletSnapshotSafe);
            _subscription = _client.Streams.WalletStream.Subscribe(HandleWalletSafe);
        }

        private void HandleWalletSafe(Wallet response)
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

        private void HandleWalletSnapshotSafe(Wallet[] response)
        {
            try
            {
                foreach (var wallet in response) HandleWallet(wallet);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Bitfinex] Failed to handle wallet info, error: '{e.Message}'");
            }
        }

        private void HandleWallet(Wallet response)
        {
            WalletChangedSubject.OnNext(new[] {ConvertWallet(response)});
        }

        private CryptoWallet ConvertWallet(Wallet response)
        {
            //var currency = response.Currency ?? "XBt";

            var wallet = new CryptoWallet
            {
                Currency = CryptoPairsHelper.Clean(response.Currency),
                Balance = response.Balance,
                //?? _lastWallet?.Balance ?? 0,
                BalanceAvailable = response.BalanceAvailable ?? _lastWallet?.BalanceAvailable ?? 0,
                Type = response.Type.ToString()
            };
            _lastWallet = wallet;
            return wallet;
        }
    }
}