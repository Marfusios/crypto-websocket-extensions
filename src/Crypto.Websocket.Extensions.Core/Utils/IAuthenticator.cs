using System.Net.Http;

namespace Crypto.Websocket.Extensions.Core.Utils
{
    public interface IAuthenticator
    {
        string ApiKey { get; }

        string UnsignedSignature { get; }

        string Passphrase { get; }

        string ComputeSignature(
            HttpMethod httpMethod, 
            string secret, 
            double timestamp, 
            string requestUri, 
            string contentBody = "");
    }
}