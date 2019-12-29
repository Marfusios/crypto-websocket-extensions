using System;

namespace Crypto.Websocket.Extensions.Core.Models
{
    /// <summary>
    /// Base class for every change/update info
    /// </summary>
    public class CryptoChangeInfo : ICryptoChangeInfo
    {
        /// <summary>
        /// Origin exchange name
        /// </summary>
        public string ExchangeName { get; set; }

        /// <summary>
        /// Server timestamp when available (only few exchanges support it)
        /// </summary>
        public DateTime? ServerTimestamp { get; set; }

        /// <summary>
        /// Server message unique sequence when available (only few exchanges support it)
        /// </summary>
        public long? ServerSequence { get; set; }
    }
}
