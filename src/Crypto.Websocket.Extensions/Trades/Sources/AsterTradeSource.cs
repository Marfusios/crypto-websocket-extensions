using System;
using System.Reactive.Linq;
using Aster.Client.Websocket.Client;
using Aster.Client.Websocket.Responses.Trades;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Trades.Models;
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    /// <summary>
    /// Aster trades source
    /// </summary>
    public class AsterTradeSource : TradeSourceBase
    {
        private AsterWebsocketClient _client = null!;
        private IDisposable? _subscription;

        /// <inheritdoc />
        public AsterTradeSource(AsterWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "aster";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(AsterWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.TradesStream
                .Where(x => x?.Data != null)
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
                _client.Logger.LogError(e, "[Aster] Failed to handle trade info, error: '{error}'", e.Message);
            }
        }

        private void HandleTrade(TradeResponse response)
        {
            if (response.Data == null)
            {
                _client.Logger.LogWarning("[Aster] Received null trade");
                return;
            }
            TradesSubject.OnNext([ConvertTrade(response.Data)]);
        }

        private CryptoTrade ConvertTrade(Trade trade)
        {
            var data = new CryptoTrade()
            {
                Amount = trade.Quantity,
                AmountQuote = trade.Quantity * trade.Price,
                Side = ConvertSide(trade.Side),
                Id = trade.TradeId.ToString(),
                Price = trade.Price,
                Timestamp = trade.TradeTime,
                Pair = trade.Symbol ?? string.Empty,
                MakerOrderId = trade.IsBuyerMaker ? trade.BuyerOrderId.ToString() : trade.SellerOrderId.ToString(),
                TakerOrderId = trade.IsBuyerMaker ? trade.SellerOrderId.ToString() : trade.BuyerOrderId.ToString(),

                ExchangeName = ExchangeName,
                ServerTimestamp = trade.EventTime
            };
            return data;
        }

        private static CryptoTradeSide ConvertSide(TradeSide tradeSide)
        {
            return tradeSide == TradeSide.Buy ? CryptoTradeSide.Buy : CryptoTradeSide.Sell;
        }
    }
}
