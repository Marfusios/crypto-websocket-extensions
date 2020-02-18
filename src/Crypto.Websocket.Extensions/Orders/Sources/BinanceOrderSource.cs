using System;
using Binance.Client.Websocket.Client;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Orders.Sources
{
    public class BinanceOrderSource : OrderSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();
        private readonly IDisposable _subscriptionCanceled;
        private readonly IDisposable _subscriptionCreated;
        private readonly IDisposable _subscriptionSnapshot;
        private readonly IDisposable _subscriptionUpdated;

        private BinanceWebsocketClient _client;

        /// <inheritdoc />
        public BinanceOrderSource(BinanceWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "binance";

        // <summary>
        /// Change client and resubscribe to the new streams
        public void ChangeClient(BinanceWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionCanceled?.Dispose();
            _subscriptionCreated?.Dispose();
            _subscriptionUpdated?.Dispose();
            _subscriptionSnapshot?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            throw new NotImplementedException();
        }
    }
}