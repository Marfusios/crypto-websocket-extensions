using System;

namespace Crypto.Websocket.Extensions.Core.Models
{
    /// <summary>
    /// Generic interface for every change/update info
    /// </summary>
    public interface ICryptoChangeInfo
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        string? ExchangeName { get; }

        /// <summary>
        /// Server timestamp when available (only few exchanges support it)
        /// </summary>
        DateTime? ServerTimestamp { get; }

        /// <summary>
        /// Server message unique sequence when available (only few exchanges support it)
        /// </summary>
        long? ServerSequence { get; }
    }
}
