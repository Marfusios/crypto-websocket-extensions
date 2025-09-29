using System;
using System.Linq;
using System.Reactive.Linq;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Trades.Models;
using Crypto.Websocket.Extensions.Core.Trades.Sources;
using Crypto.Websocket.Extensions.Core.Validations;
using Hyperliquid.Client.Websocket.Client;
using Hyperliquid.Client.Websocket.Enums;
using Hyperliquid.Client.Websocket.Responses;
using Microsoft.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Trades.Sources
{
    /// <summary>
    /// Hyperliquid trades source
    /// </summary>
    public class HyperliquidTradeSource : TradeSourceBase
    {
        private HyperliquidWebsocketClient _client = null!;
        private IDisposable? _subscription;

        /// <inheritdoc />
        public HyperliquidTradeSource(HyperliquidWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "hyperliquid";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(HyperliquidWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.TradesStream
                .Where(x => x.Length > 0)
                .Subscribe(HandleTradeSafe);
        }

        private void HandleTradeSafe(TradeResponse[] response)
        {
            try
            {
                HandleTrades(response);
            }
            catch (Exception e)
            {
                _client.Logger.LogError(e, "[Bitfinex] Failed to handle trade info, error: '{error}'", e.Message);
            }
        }

        private void HandleTrades(TradeResponse[] response)
        {
            var trades = response.Select(ConvertTrade).ToArray();
            TradesSubject.OnNext(trades);
        }

        private CryptoTrade ConvertTrade(TradeResponse trade)
        {
            var buyerId = trade.Users.Length > 0 ? trade.Users[0] : null;
            var sellerId = trade.Users.Length > 1 ? trade.Users[1] : null;
            
            var data = new CryptoTrade
            {
                Amount = trade.Size,
                AmountQuote = trade.Size * trade.Price,
                Side = ConvertSide(trade.Side),
                Id = trade.TradeId.ToString(),
                Price = trade.Price,
                Timestamp = trade.Time,
                Pair = trade.Coin,
                MakerOrderId = trade.Side == Side.Bid ? sellerId : buyerId,
                TakerOrderId = trade.Side == Side.Bid ? buyerId : sellerId,

                ExchangeName = ExchangeName
            };
            return data;
        }

        private static CryptoTradeSide ConvertSide(Side side)
        {
            return side switch
            {
                Side.Bid => CryptoTradeSide.Buy,
                Side.Ask => CryptoTradeSide.Sell,
                _ => CryptoTradeSide.Undefined
            };
        }
    }
}
