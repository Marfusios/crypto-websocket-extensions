using System;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Utils;

namespace Crypto.Websocket.Extensions.Core.Positions.Models
{
    /// <summary>
    /// Information about currently open position
    /// </summary>
    [DebuggerDisplay("Position: {Pair} - {EntryPrice} {Amount}/{AmountQuote} - pnl: {UnrealizedPnl}")]
    public class CryptoPosition
    {
        private double _amount;
        private double _amountQuote;
        private CryptoPositionSide _side;

        /// <summary>
        /// Pair to which this position belongs
        /// </summary>
        public string Pair { get; set; }

        /// <summary>
        /// Pair to which this position belongs (cleaned)
        /// </summary>
        public string PairClean => CryptoPairsHelper.Clean(Pair);

        /// <summary>
        /// Position's opening timestamp
        /// </summary>
        public DateTime? OpeningTimestamp { get; set; }

        /// <summary>
        /// Position's current timestamp
        /// </summary>
        public DateTime? CurrentTimestamp { get; set; }

        /// <summary>
        /// Position's entry price
        /// </summary>
        public double EntryPrice { get; set; }

        /// <summary>
        /// Market's last price
        /// </summary>
        public double LastPrice { get; set; }

        /// <summary>
        /// Market's mark price - used for liquidation
        /// </summary>
        public double MarkPrice { get; set; }

        /// <summary>
        /// Position's liquidation price
        /// </summary>
        public double LiquidationPrice { get; set; }

        /// <summary>
        /// Original position amount (stable) in base currency
        /// </summary>
        public double Amount
        {
            get => _amount;
            set => _amount = WithCorrectSign(value);
        }

        /// <summary>
        /// Original order amount (stable) in quote currency
        /// </summary>
        public double AmountQuote
        {
            get => _amountQuote;
            set => _amountQuote = WithCorrectSign(value);
        }

        /// <summary>
        /// Position's side
        /// </summary>
        public CryptoPositionSide Side
        {
            get => _side;
            set
            {
                _side = value;

                _amount = WithCorrectSign(_amount);
                _amountQuote = WithCorrectSign(_amountQuote);
            }
        }

        /// <summary>
        /// Current leverage (supported only by few exchanges)
        /// </summary>
        public double? Leverage { get; set; }

        /// <summary>
        /// Realized position profit (supported only by few exchanges)
        /// </summary>
        public double? RealizedPnl { get; set; }

        /// <summary>
        /// Unrealized position profit (supported only by few exchanges)
        /// </summary>
        public double? UnrealizedPnl { get; set; }


        private double WithCorrectSign(double value)
        {
            if (_side == CryptoPositionSide.Undefined) return value;

            return Math.Abs(value) * (_side == CryptoPositionSide.Long ? 1 : -1);
        }
    }
}