using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Responses.Wallets;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Core.Wallets.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Sources;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Wallets.Sources
{
    /// <summary>
    /// Coinbase Pro wallet source
    /// </summary>
    public class CoinbaseWalletSource : WalletSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private CoinbaseWebsocketClient _client;
        private IDisposable _subscription;
        private CryptoWallet _lastWallet;
        private IDisposable _subscriptionSnapshot;

        /// <inheritdoc />
        public CoinbaseWalletSource(CoinbaseWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "coinbase";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(CoinbaseWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.WalletsSnapshotStream.Subscribe(HandleSnapshot);
            _subscription = _client.Streams.WalletStream.Subscribe(HandleWalletSafe);
        }

        private void HandleWalletSafe(WalletResponse response)
        {
            try
            {
                HandleWallet(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Coinbase] Failed to handle wallet info, error: '{e.Message}'");
            }
        }

        /// <summary>
        /// Request a new order book snapshot, will be fakely streamed via communicator (WebsocketClient)
        /// Method doesn't throw exception, just logs it
        private void HandleSnapshot(WalletsSnapshotResponse snapshot)
        {
            foreach (var wallet in snapshot.Wallets) HandleWallet(wallet);
        }

        private void HandleWallet(WalletResponse response)
        {
            WalletChangedSubject.OnNext(new[] {ConvertWallet(response)});
        }

        private CryptoWallet ConvertWallet(WalletResponse response)
        {
            var id = response.Currency;

            var wallet = new CryptoWallet
            {
                Type = "Exchange",
                Currency = CryptoPairsHelper.Clean(response.Currency),
                Balance = Math.Abs(response.Balance),
                BalanceAvailable = response.Available
            };

            _lastWallet = wallet;
            return wallet;
        }
    }
}