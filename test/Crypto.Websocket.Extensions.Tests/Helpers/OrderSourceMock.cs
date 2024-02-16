using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Orders.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crypto.Websocket.Extensions.Tests.Helpers
{
    public class OrderSourceMock : OrderSourceBase
    {
        public OrderSourceMock() : base(NullLogger.Instance)
        {
        }

        public override string ExchangeName => "mock";

        public void StreamOrder(CryptoOrder order)
        {
            OrderUpdatedSubject.OnNext(order);
        }
    }
}
