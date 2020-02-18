using System;
using Crypto.Websocket.Extensions.Core.Trades.Models;

namespace Crypto.Websocket.Extensions.Core.Trades.Sources
{
    /// <summary>
    /// Source that provides info about executed trades
    /// </summary>
    public interface ITradeSource
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Stream info about executed trades
        /// </summary>
        IObservable<CryptoTrade[]> TradesStream { get; }
    }
}