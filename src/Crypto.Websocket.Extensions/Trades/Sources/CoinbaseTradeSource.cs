using System;
using System.Reactive.Linq;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Responses.Trades;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Trades.Models;
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    /// <summary>
    /// Coinbase trades source
    /// </summary>
    public class CoinbaseTradeSource : TradeSourceBase
    {
        private CoinbaseWebsocketClient _client = null!;
        private IDisposable? _subscription;

        /// <inheritdoc />
        public CoinbaseTradeSource(CoinbaseWebsocketClient client)
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
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.TradesStream
                .Where(x => x != null)
                .Subscribe(HandleTradeSafe);
        }

        private void HandleTradeSafe(TradeResponse response)
        {
            try
            {
                HandleTrade(response);
            }
            catch (Exception e)
            {
                _client.Logger.LogError(e, "[Coinbase] Failed to handle trade info, error: '{error}'", e.Message);
            }
        }

        private void HandleTrade(TradeResponse response)
        {
            TradesSubject.OnNext(new[] { ConvertTrade(response) });
        }

        private CryptoTrade ConvertTrade(TradeResponse trade)
        {
            var data = new CryptoTrade()
            {
                Amount = trade.Size,
                AmountQuote = trade.Size * trade.Price,
                Side = ConvertSide(trade.TradeSide),
                Id = trade.TradeId.ToString(),
                Price = trade.Price,
                Timestamp = trade.Time,
                Pair = trade.ProductId,
                MakerOrderId = trade.MakerOrderId,
                TakerOrderId = trade.TakerOrderId,

                ExchangeName = ExchangeName,
                ServerSequence = trade.Sequence,
                ServerTimestamp = trade.Time
            };
            return data;
        }

        private CryptoTradeSide ConvertSide(TradeSide tradeSide)
        {
            if (tradeSide == TradeSide.Undefined)
                return CryptoTradeSide.Undefined;
            return tradeSide == TradeSide.Buy ? CryptoTradeSide.Buy : CryptoTradeSide.Sell;
        }
    }
}
