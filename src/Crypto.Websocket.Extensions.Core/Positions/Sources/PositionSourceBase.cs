using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Crypto.Websocket.Extensions.Core.Positions.Models;

namespace Crypto.Websocket.Extensions.Core.Positions.Sources
{
    /// <summary>
    /// Source that provides info about currently opened positions
    /// </summary>
    public abstract class PositionSourceBase : IPositionSource
    {
        /// <summary>
        /// Position subject
        /// </summary>
        protected readonly Subject<CryptoPosition[]> PositionsSubject = new Subject<CryptoPosition[]>();


        /// <summary>
        /// Origin exchange name
        /// </summary>
        public abstract string ExchangeName { get; }

        /// <summary>
        /// Stream info about current positions
        /// </summary>
        public virtual IObservable<CryptoPosition[]> PositionsStream => PositionsSubject.AsObservable();
    }
}