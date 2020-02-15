using System;
using Crypto.Websocket.Extensions.Core.Positions.Models;

namespace Crypto.Websocket.Extensions.Core.Positions.Sources
{
    /// <summary>
    /// Source that provides info about currently opened positions
    /// </summary>
    public interface IPositionSource
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        string ExchangeName { get; }

        /// <summary>
        /// Stream info about current positions
        /// </summary>
        IObservable<CryptoPosition[]> PositionsStream { get; }
    }
}