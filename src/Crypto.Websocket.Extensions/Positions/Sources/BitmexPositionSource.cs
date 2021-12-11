using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reactive.Linq;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Responses.Positions;
using Bitmex.Client.Websocket.Utils;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Positions.Models;
using Crypto.Websocket.Extensions.Core.Positions.Sources;
using Crypto.Websocket.Extensions.Core.Utils;
using Crypto.Websocket.Extensions.Core.Validations;
using Crypto.Websocket.Extensions.Logging;

namespace Crypto.Websocket.Extensions.Positions.Sources
{
    /// <summary>
    /// Bitmex positions source
    /// </summary>
    public class BitmexPositionSource : PositionSourceBase
    {
        static readonly ILog Log = LogProvider.GetCurrentClassLogger();
        readonly ConcurrentDictionary<string, CryptoPosition> _positions = new();

        IBitmexWebsocketClient _client;
        IDisposable _subscription;

        /// <inheritdoc />
        public BitmexPositionSource(IBitmexWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitmex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(IBitmexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        void Subscribe()
        {
            _subscription = _client.Streams.PositionStream
                .Where(x => x?.Data != null && x.Data.Any())
                .Subscribe(HandleSafe);
        }

        void HandleSafe(PositionResponse response)
        {
            try
            {
                Handle(response);
            }
            catch (Exception e)
            {
                Log.Error(e, $"[Bitmex] Failed to handle position info, error: '{e.Message}'");
            }
        }

        void Handle(PositionResponse response)
        {
            PositionsSubject.OnNext(Convert(response.Data));
        }

        CryptoPosition[] Convert(Position[] positions)
        {
            return positions.Select(Convert).ToArray();
        }

        CryptoPosition Convert(Position position)
        {
            var key = GetPositionKey(position);
            var existing = _positions.ContainsKey(key) ? _positions[key] : null;

            var currency = position.Currency ?? "XBt";

            var current = new CryptoPosition()
            {
                Pair = position.Symbol ?? existing?.Pair,
                CurrentTimestamp = position.CurrentTimestamp ?? existing?.CurrentTimestamp,
                OpeningTimestamp = position.OpeningTimestamp ?? existing?.OpeningTimestamp,

                EntryPrice = position.AvgEntryPrice ?? existing?.EntryPrice ?? 0,
                LastPrice = position.LastPrice ?? existing?.LastPrice ?? 0,
                MarkPrice = position.MarkPrice ?? existing?.MarkPrice ?? 0,
                LiquidationPrice = position.LiquidationPrice ?? existing?.LiquidationPrice ?? 0,

                Amount = position.HomeNotional ?? existing?.Amount ?? 0,
                AmountQuote = position.CurrentQty ?? existing?.AmountQuote ??  0,

                Side = ConvertSide(position.CurrentQty ?? existing?.AmountQuote),

                Leverage = position.Leverage ?? existing?.Leverage,
                RealizedPnl = ConvertToBtc(currency, position.RealisedPnl) ?? existing?.RealizedPnl,
                UnrealizedPnl = ConvertToBtc(currency, position.UnrealisedPnl) ?? existing?.UnrealizedPnl,
            };

            _positions[key] = current;
            return current;
        }

        static CryptoPositionSide ConvertSide(double? amount)
        {
            if (!amount.HasValue || CryptoMathUtils.IsSame(amount.Value, 0))
                return CryptoPositionSide.Undefined;
            return amount.Value >= 0 ? CryptoPositionSide.Long : CryptoPositionSide.Short;
        }

        static double? ConvertToBtc(string currency, double? value)
        {
            if (!value.HasValue)
                return null;

            return BitmexConverter.ConvertToBtc(currency, value.Value);
        }

        static string GetPositionKey(Position position)
        {
            return $"{position.Symbol}-{position.Account}";
        }
    }
}
