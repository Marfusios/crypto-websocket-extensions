using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;

namespace Crypto.Websocket.Extensions.Tests.Helpers
{
    public class OrderSourceMock : OrderSourceBase
    {
        public override string ExchangeName => "mock";

        public void StreamOrder(CryptoOrder order)
        {
            OrderUpdatedSubject.OnNext(order);
        }
    }
}
