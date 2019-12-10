using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Crypto.Websocket.Extensions.Core.Trades.Models;

namespace Crypto.Websocket.Extensions.Core.Trades.Sources
{
    /// <summary>
    /// Source that provides info about executed trades 
    /// </summary>
    public abstract class TradeSourceBase : ITradeSource
    {
        /// <summary>
        /// Trades subject
        /// </summary>
        protected readonly Subject<CryptoTrade[]> TradesSubject = new Subject<CryptoTrade[]>();


        /// <summary>
        /// Origin exchange name
        /// </summary>
        public abstract string ExchangeName { get; }

        /// <summary>
        /// Stream info about executed trades
        /// </summary>
        public virtual IObservable<CryptoTrade[]> TradesStream => TradesSubject.AsObservable();
    }
}
