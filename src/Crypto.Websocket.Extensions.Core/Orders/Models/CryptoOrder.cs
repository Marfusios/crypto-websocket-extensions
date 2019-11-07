using System;
using System.Diagnostics;
using Crypto.Websocket.Extensions.Core.Models;
using Crypto.Websocket.Extensions.Core.Utils;

namespace Crypto.Websocket.Extensions.Core.Orders.Models
{
    /// <summary>
    /// Order snapshot
    /// </summary>
    [DebuggerDisplay("Order: {Id}/{ClientId} - {Pair} - {PriceGrouped} {AmountFilled}")]
    public class CryptoOrder
    {
        private CryptoOrderSide _side;
        private double? _amountFilledCumulative;
        private double? _amountFilledCumulativeQuote;
        private double? _amountFilled;
        private double? _amountFilledQuote;
        private double? _amountOrig;
        private double? _amountOrigQuote;

        /// <summary>
        /// Unique order id (provided by exchange)
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Group id of related orders (provided by client, supported only by a few exchanges)
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// Unique client order id (provided by client)
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Pair to which this order belongs
        /// </summary>
        public string Pair { get; set; }

        /// <summary>
        /// Pair to which this order belongs (cleaned)
        /// </summary>
        public string PairClean => CryptoPairsHelper.Clean(Pair);

        /// <summary>
        /// Order's created timestamp
        /// </summary>
        public DateTime? Created { get; set; }

        /// <summary>
        /// Order's updated timestamp
        /// </summary>
        public DateTime? Updated { get; set; }


        /// <summary>
        /// Order's side
        /// </summary>
        public CryptoOrderSide Side
        {
            get => _side;
            set
            {
                _side = value;

                if (_amountOrig.HasValue)
                {
                    _amountOrig = WithCorrectSign(_amountOrig);
                }

                if (_amountFilled.HasValue)
                {
                    _amountFilled = WithCorrectSign(_amountFilled);
                }
            }
        }

        /// <summary>
        /// Filled amount for this single order in base currency
        /// </summary>
        public double? AmountFilled
        {
            get => _amountFilled;
            set => _amountFilled = WithCorrectSign(value);
        }

        /// <summary>
        /// Filled amount for this single order in quote currency
        /// </summary>
        public double? AmountFilledQuote
        {
            get => _amountFilledQuote;
            set => _amountFilledQuote = WithCorrectSign(value);
        }

        /// <summary>
        /// Cumulative filled amount for this particular order (all partial fills together) in base currency
        /// </summary>
        public double? AmountFilledCumulative
        {
            get => _amountFilledCumulative;
            set => _amountFilledCumulative = WithCorrectSign(value);
        }

        /// <summary>
        /// Cumulative filled amount for this particular order (all partial fills together) in quote currency
        /// </summary>
        public double? AmountFilledCumulativeQuote
        {
            get => _amountFilledCumulativeQuote;
            set => _amountFilledCumulativeQuote = WithCorrectSign(value);
        }

        /// <summary>
        /// Original order amount (stable) in base currency
        /// </summary>
        public double? AmountOrig
        {
            get => (_amountOrig);
            set => _amountOrig = WithCorrectSign(value);
        }

        /// <summary>
        /// Original order amount (stable) in quote currency
        /// </summary>
        public double? AmountOrigQuote
        {
            get => (_amountOrigQuote);
            set => _amountOrigQuote = WithCorrectSign(value);
        }

        /// <summary>
        /// Order's type
        /// </summary>
        public CryptoOrderType Type { get; set; }

        /// <summary>
        /// Order's previous type (in case of update)
        /// </summary>
        public CryptoOrderType TypePrev { get; set; }

        /// <summary>
        /// Current order's status
        /// </summary>
        public CryptoOrderStatus OrderStatus { get; set; }

        /// <summary>
        /// Order's price
        /// </summary>
        public double? Price { get; set; }

        /// <summary>
        /// Order's average price
        /// </summary>
        public double? PriceAverage { get; set; }

        /// <summary>
        /// Order's price (average or current)
        /// </summary>
        public double PriceGrouped => FirstNotNull(PriceAverage, Price);

        /// <summary>
        /// Order's amount (filled or original)
        /// </summary>
        public double AmountGrouped => FirstNotNull(AmountFilled, AmountOrig);

        /// <summary>
        /// Difference between original and filled
        /// </summary>
        public double AmountDiff => Math.Abs(AmountOrig ?? 0) - Math.Abs(AmountFilledCumulative ?? AmountFilled ?? 0);

        /// <summary>
        /// Whenever order was executed on margin
        /// </summary>
        public bool OnMargin { get; set; }




        private double? WithCorrectSign(double? value)
        {
            if (!value.HasValue || _side == CryptoOrderSide.Undefined)
                return value;
            return Math.Abs(value.Value) * (_side == CryptoOrderSide.Bid ? 1 : -1);
        }

        private double FirstNotNull(params double?[] numbers)
        {
            foreach (var number in numbers)
            {
                if (number.HasValue && Math.Abs(number.Value) > 0)
                    return number.Value;
            }

            return 0;
        }


        /// <summary>
        /// Return order side based on amount sign
        /// </summary>
        public static CryptoOrderSide RecognizeSide(double amount)
        {
            if (CryptoMathUtils.IsSame(amount, 0))
                return CryptoOrderSide.Undefined;
            return amount >= 0 ? CryptoOrderSide.Bid : CryptoOrderSide.Ask;
        }

        /// <summary>
        /// Return order side based on amount sign
        /// </summary>
        public static CryptoOrderSide RecognizeSide(double? amount)
        {
            if (!amount.HasValue || CryptoMathUtils.IsSame(amount.Value, 0))
                return CryptoOrderSide.Undefined;
            return amount >= 0 ? CryptoOrderSide.Bid : CryptoOrderSide.Ask;
        }

        /// <summary>
        /// Create fake order (mock)
        /// </summary>
        public static CryptoOrder Mock(string cid, string pair, CryptoOrderSide side,
            double price, double amount, CryptoOrderType type, DateTime timestamp)
        {
            return new CryptoOrder
            {
                Id = Guid.NewGuid().ToString("D"),
                ClientId = cid,
                Pair = pair,
                Price = price,
                PriceAverage = price,
                AmountFilled = amount,
                AmountOrig = amount,
                Side = side,
                Updated = timestamp,
                Created = timestamp,
                Type = type,
                TypePrev = type,
                OrderStatus = CryptoOrderStatus.Executed
            };
        }
    }
}
