using System;

namespace Crypto.Websocket.Extensions.Exceptions
{
    /// <summary>
    /// Exception to cover bad user input
    /// </summary>
    public class CryptoBadInputException : CryptoException
    {
        /// <inheritdoc />
        public CryptoBadInputException()
        {
        }

        /// <inheritdoc />
        public CryptoBadInputException(string message) : base(message)
        {
        }

        /// <inheritdoc />
        public CryptoBadInputException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
