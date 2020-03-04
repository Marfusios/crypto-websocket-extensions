using System;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Crypto.Websocket.Extensions.Core.Utils
{
    public class Authenticator: IAuthenticator
    {
        public Authenticator(string apiKey, string unsignedSignature, string passphrase)
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(unsignedSignature) || string.IsNullOrEmpty(passphrase))
                throw new ArgumentException("Authenticator requires parameters apiKey, unsignedSignature and passphrase to be populated.");
            this.ApiKey = apiKey;
            this.UnsignedSignature = unsignedSignature;
            this.Passphrase = passphrase;
        }

        public string ApiKey { get; }

        public string UnsignedSignature { get; }

        public string Passphrase { get; }

        public string ComputeSignature(
            HttpMethod httpMethod,
            string secret,
            double timestamp,
            string requestUri,
            string contentBody = "")
        {
            byte[] secret1 = Convert.FromBase64String(secret);
            return this.HashString(timestamp.ToString("F0", (IFormatProvider) CultureInfo.InvariantCulture) + httpMethod.ToString().ToUpper() + requestUri + contentBody, secret1);
        }

        private string HashString(string str, byte[] secret)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            using (HMACSHA256 hmacshA256 = new HMACSHA256(secret))
                return Convert.ToBase64String(hmacshA256.ComputeHash(bytes));
        }
    }
}
