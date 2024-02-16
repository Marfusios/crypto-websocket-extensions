using System;
using System.Reactive.Linq;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Responses.Trades;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Trades.Models;
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    /// <summary>
    /// Bitfinex trades source
    /// </summary>
    public class BitfinexTradeSource : TradeSourceBase
    {
        private BitfinexWebsocketClient _client = null!;
        private IDisposable? _subscription;

        /// <inheritdoc />
        public BitfinexTradeSource(BitfinexWebsocketClient client)
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
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.TradesStream
                .Where(x => x != null && x.Type == TradeType.Executed)
                .Subscribe(HandleTradeSafe);
        }

        private void HandleTradeSafe(Trade response)
        {
            try
            {
                HandleTrade(response);
            }
            catch (Exception e)
            {
                _client.Logger.LogError(e, "[Bitfinex] Failed to handle trade info, error: '{error}'", e.Message);
            }
        }

        private void HandleTrade(Trade response)
        {
            TradesSubject.OnNext(new[] { ConvertTrade(response) });
        }

        private CryptoTrade ConvertTrade(Trade trade)
        {
            var data = new CryptoTrade()
            {
                Amount = trade.Amount,
                AmountQuote = trade.Amount * trade.Price,
                Side = ConvertSide(trade.Amount),
                Id = trade.Id.ToString(),
                Price = trade.Price,
                Timestamp = trade.Mts,
                Pair = trade.Pair,

                ExchangeName = ExchangeName,
                ServerSequence = trade.ServerSequence,
                ServerTimestamp = trade.ServerTimestamp
            };
            return data;
        }

        private CryptoTradeSide ConvertSide(double amount)
        {
            return amount >= 0 ? CryptoTradeSide.Buy : CryptoTradeSide.Sell;
        }
    }
}
