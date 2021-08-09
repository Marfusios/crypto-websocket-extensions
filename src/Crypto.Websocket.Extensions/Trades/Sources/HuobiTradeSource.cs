using System;
using System.Linq;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Trades.Models;
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;
using Huobi.Client.Websocket.Clients;
using Huobi.Client.Websocket.Messages.MarketData;
using Huobi.Client.Websocket.Messages.MarketData.MarketTradeDetail;
using Huobi.Client.Websocket.Messages.MarketData.Values;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    public class HuobiTradeSource : TradeSourceBase
    {
        private static readonly ILog Log = LogProvider.GetCurrentClassLogger();

        private IHuobiMarketWebsocketClient _client;
        private IDisposable _subscription;

        public HuobiTradeSource(IHuobiMarketWebsocketClient client)
        {
            ChangeClient(client);
        }

        public override string ExchangeName => "huobi";

        public void ChangeClient(IHuobiMarketWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client
                .Streams
                .TradeDetailUpdateStream
                .Subscribe(HandleTradeSafe);
        }

        private void HandleTradeSafe(MarketTradeDetailUpdateMessage response)
        {
            try
            {
                HandleTrade(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Huobi] Failed to handle trade info, error: '{e.Message}'");
            }
        }

        private void HandleTrade(MarketTradeDetailUpdateMessage response)
        {
            var converted = response
                .Tick
                .Data
                .Select(
                    trade => ConvertTrade(
                        response.ParseSymbolFromTopic(),
                        trade,
                        response.Timestamp.UtcDateTime))
                .ToArray();

            TradesSubject.OnNext(converted);
        }

        private CryptoTrade ConvertTrade(string symbol, MarketTradeDetailTickDataItem trade, DateTime serverTimestamp)
        {
            var data = new CryptoTrade
            {
                Amount = (double)trade.Amount,
                AmountQuote = (double)(trade.Amount * trade.Price),
                Side = ConvertSide(trade.Direction),
                Id = trade.TradeId.ToString(),
                Price = (double)trade.Price,
                Timestamp = trade.Timestamp.UtcDateTime,
                Pair = symbol,

                ExchangeName = ExchangeName,
                ServerTimestamp = serverTimestamp
            };
            return data;
        }

        private CryptoTradeSide ConvertSide(TradeSide tradeSide)
        {
            return tradeSide == TradeSide.Buy
                ? CryptoTradeSide.Buy
                : CryptoTradeSide.Sell;
        }
    }
}