using System;
using System.Linq;
using System.Reactive.Linq;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Responses;
using Bitmex.Client.Websocket.Responses.Trades;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Trades.Models;
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    /// <summary>
    /// Bitmex trades source
    /// </summary>
    public class BitmexTradeSource : TradeSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private BitmexWebsocketClient _client;
        private IDisposable _subscription;

        /// <inheritdoc />
        public BitmexTradeSource(BitmexWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitmex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitmexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.TradesStream
                .Where(x => x?.Data != null && x.Data.Any())
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
                Log.Error(e, $"[Bitmex] Failed to handle trade info, error: '{e.Message}'");
            }
        }

        private void HandleTrade(TradeResponse response)
        {
            TradesSubject.OnNext(ConvertTrades(response.Data));
        }

        private CryptoTrade[] ConvertTrades(Trade[] trades)
        {
            return trades.Select(ConvertTrade).ToArray();
        }

        private CryptoTrade ConvertTrade(Trade trade)
        {
            var data = new CryptoTrade
            {
                Amount = trade.Size / trade.Price,
                AmountQuote = trade.Size,
                Side = ConvertSide(trade.Side),
                Id = trade.TrdMatchId,
                Price = trade.Price,
                Timestamp = trade.Timestamp,
                Pair = trade.Symbol,

                ExchangeName = ExchangeName
            };
            return data;
        }

        private CryptoTradeSide ConvertSide(BitmexSide tradeSide)
        {
            if (tradeSide == BitmexSide.Undefined) return CryptoTradeSide.Undefined;

            return tradeSide == BitmexSide.Buy ? CryptoTradeSide.Buy : CryptoTradeSide.Sell;
        }
    }
}