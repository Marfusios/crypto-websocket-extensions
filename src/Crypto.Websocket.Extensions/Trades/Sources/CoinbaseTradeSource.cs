using System;
using System.Reactive.Linq;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Responses;
using Coinbase.Client.Websocket.Responses.Full;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Trades.Models;
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    /// <summary>
    /// Coinbase trades source
    /// </summary>
    public class CoinbaseTradeSource : TradeSourceBase
    {
        static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        ICoinbaseWebsocketClient _client;
        IDisposable _subscription;

        /// <inheritdoc />
        public CoinbaseTradeSource(ICoinbaseWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "coinbase";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(ICoinbaseWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        void Subscribe()
        {
            _subscription = _client.Streams.MatchesStream
                .Where(x => x != null)
                .Subscribe(HandleTradeSafe);
        }

        void HandleTradeSafe(MatchResponse response)
        {
            try
            {
                HandleTrade(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Coinbase] Failed to handle trade info, error: '{e.Message}'");
            }
        }

        void HandleTrade(MatchResponse response)
        {
            TradesSubject.OnNext(new[] { ConvertTrade(response) });
        }

        CryptoTrade ConvertTrade(MatchResponse trade)
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

        static CryptoTradeSide ConvertSide(TradeSide tradeSide)
        {
            if (tradeSide == TradeSide.Undefined)
                return CryptoTradeSide.Undefined;
            return tradeSide == TradeSide.Buy ? CryptoTradeSide.Buy : CryptoTradeSide.Sell;
        }
    }
}
