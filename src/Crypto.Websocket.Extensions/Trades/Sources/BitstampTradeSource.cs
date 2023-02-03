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
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    /// <summary>
    /// Bitstamp trade source
    /// </summary>
    public class BitstampTradeSource : TradeSourceBase
    {
        static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        IBitstampWebsocketClient _client;
        IDisposable _subscription;

        /// <inheritdoc />
        public BitstampTradeSource(IBitstampWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitstamp";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(IBitstampWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        void Subscribe()
        {
            _subscription = _client.Streams.TickerStream
                .Where(x => x?.Data != null && x.Data.Side != TradeSide.Undefined)
                .Subscribe(HandleTradeSafe);
        }

        void HandleTradeSafe(TradeResponse response)
        {
            try
            {
                HandleTrade(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Bitstamp] Failed to handle trade info, error: '{e.Message}'");
            }
        }

        void HandleTrade(TradeResponse response)
        {
            TradesSubject.OnNext(new[] { ConvertTrade(response) });
        }

        CryptoTrade ConvertTrade(TradeResponse trade)
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

        static CryptoTradeSide ConvertSide(TradeSide side)
        {
            return side == TradeSide.Buy ? CryptoTradeSide.Buy : CryptoTradeSide.Sell;
        }
    }
}
