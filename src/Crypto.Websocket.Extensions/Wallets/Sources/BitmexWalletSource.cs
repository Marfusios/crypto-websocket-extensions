using System;
using System.Linq;
using System.Reactive.Linq;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Responses.Margins;
using Bitmex.Client.Websocket.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Core.Wallets.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Sources;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Wallets.Sources
{
    /// <summary>
    /// Bitmex wallet source
    /// </summary>
    public class BitmexWalletSource : WalletSourceBase
    {
        static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        IBitmexWebsocketClient _client;
        IDisposable _subscription;
        CryptoWallet _lastWallet;

        /// <inheritdoc />
        public BitmexWalletSource(IBitmexWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitmex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(IBitmexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        void Subscribe()
        {
            _subscription = _client.Streams.MarginStream
                .Where(x => x?.Data != null && x.Data.Any())
                .Subscribe(HandleWalletSafe);
        }

        void HandleWalletSafe(MarginResponse response)
        {
            try
            {
                HandleWallet(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Bitmex] Failed to handle wallet info, error: '{e.Message}'");
            }
        }

        void HandleWallet(MarginResponse response)
        {
            WalletChangedSubject.OnNext(response.Data.Select(ConvertWallet).ToArray());
        }

        CryptoWallet ConvertWallet(Margin margin)
        {
            var currency = margin.Currency ?? "XBt";

            var wallet = new CryptoWallet()
            {
                Currency = "BTC",
                Balance = ConvertToBtc(currency, margin.WalletBalance) ?? _lastWallet?.Balance ?? 0,
                BalanceAvailable = ConvertToBtc(currency, margin.AvailableMargin) ?? _lastWallet?.BalanceAvailable ?? 0,
                Leverage = margin.MarginLeverage ?? _lastWallet?.Leverage,
                RealizedPnl = ConvertToBtc(currency, margin.RealisedPnl) ?? _lastWallet?.RealizedPnl,
                UnrealizedPnl = ConvertToBtc(currency, margin.UnrealisedPnl) ?? _lastWallet?.UnrealizedPnl,
                Type = margin.Account.ToString()
            };
            _lastWallet = wallet;
            return wallet;
        }

        static double? ConvertToBtc(string currency, long? value)
        {
            if (!value.HasValue)
                return null;

            return BitmexConverter.ConvertToBtc(currency, value.Value);
        }
    }
}
