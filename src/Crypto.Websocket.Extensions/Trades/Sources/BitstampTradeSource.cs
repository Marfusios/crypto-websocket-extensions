using System;
using System.Globalization;
using System.Reactive.Linq;
using Bitstamp.Client.Websocket.Client;
using Bitstamp.Client.Websocket.Responses;
using Bitstamp.Client.Websocket.Responses.Trades;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Trades.Models;
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    /// <summary>
    /// Bitstamp trade source
    /// </summary>
    public class BitstampTradeSource : TradeSourceBase
    {
        private BitstampWebsocketClient _client = null!;
        private IDisposable? _subscription;

        /// <inheritdoc />
        public BitstampTradeSource(BitstampWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitstamp";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitstampWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.TickerStream
                .Where(x => x?.Data != null && x.Data.Side != TradeSide.Undefined)
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
                _client.Logger.LogError(e, "[Bitstamp] Failed to handle trade info, error: '{error}'", e.Message);
            }
        }

        private void HandleTrade(TradeResponse response)
        {
            TradesSubject.OnNext(new[] { ConvertTrade(response) });
        }

        private CryptoTrade ConvertTrade(TradeResponse trade)
        {
            var data = trade.Data;

            var buyId = data.BuyOrderId.ToString(CultureInfo.InvariantCulture);
            var sellId = data.SellOrderId.ToString(CultureInfo.InvariantCulture);

            var result = new CryptoTrade()
            {
                Amount = data.Amount,
                AmountQuote = data.Amount * data.Price,
                Side = ConvertSide(data.Side),
                Id = data.Id.ToString(CultureInfo.InvariantCulture),
                Price = data.Price,
                Timestamp = data.Microtimestamp,
                Pair = trade.Symbol,

                ExchangeName = ExchangeName,
                ServerTimestamp = data.Microtimestamp,

                MakerOrderId = data.Side == TradeSide.Buy ? sellId : buyId,
                TakerOrderId = data.Side == TradeSide.Sell ? sellId : buyId
            };
            return result;
        }

        private CryptoTradeSide ConvertSide(TradeSide side)
        {
            return side == TradeSide.Buy ? CryptoTradeSide.Buy : CryptoTradeSide.Sell;
        }
    }
}
