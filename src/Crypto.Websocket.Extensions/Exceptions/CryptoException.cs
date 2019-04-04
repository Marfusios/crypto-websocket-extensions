using System;

namespace Crypto.Websocket.Extensions.Exceptions
{
    /// <summary>
    /// Base exception for this library
    /// </summary>
    public class CryptoException : Exception
    {
        /// <inheritdoc />
        public CryptoException()
        {
        }

        /// <inheritdoc />
        public CryptoException(string message)
            : base(message)
        {
        }

        /// <inheritdoc />
        public CryptoException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
