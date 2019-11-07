using System;
using System.Threading.Tasks;
using Bitmex.Client.Websocket;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Orders.Sources;
using Serilog;

namespace Crypto.Websocket.Extensions.Sample
{
    public static class OrdersExample
    {
        public static async Task RunEverything()
        {
            var ordBitmex = await StartBitmex(true, HandleOrderChanged);

            Log.Information("Waiting for orders...");
        }

        private static void HandleOrderChanged(CryptoOrder order)
        {
            Log.Information($"Order '{order.ClientId}' [{order.Pair} {order.Side} {order.Type}] changed. " +
                            $"Price: {order.PriceGrouped}, Amount: {order.AmountOrig:#.#####}/{order.AmountOrigQuote}, " +
                            $"cumulative: {order.AmountFilledCumulative:#.#####}/{order.AmountFilledCumulativeQuote}, " +
                            $"filled: {order.AmountFilled:#.#####}/{order.AmountFilledQuote}, " +
                            $"Status: {order.OrderStatus}");
        }




        private static async Task<ICryptoOrders> StartBitmex(bool isTestnet, Action<CryptoOrder> handler)
        {
            var key = "r6GfUxQKEjoxZIayHOe9hYWq";
            var secret = "yeXzg7sBqSDNdpwPo5UzclDd2L-4DwgDkVgYnYoDMEMOjhtg";

            var url = isTestnet ? BitmexValues.ApiWebsocketTestnetUrl : BitmexValues.ApiWebsocketUrl;
            var communicator = new BitmexWebsocketCommunicator(url) { Name = "Bitmex" };
            var client = new BitmexWebsocketClient(communicator);

            var source = new BitmexOrderSource(client);
            var orders = new CryptoOrders(source);

            orders.OrderChangedStream.Subscribe(handler);

            client.Streams.AuthenticationStream.Subscribe(x =>
            {
                Log.Information($"[Bitmex] Authenticated '{x.Success}'");
                client.Send(new Bitmex.Client.Websocket.Requests.OrderSubscribeRequest()).Wait();
            });

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                client.Authenticate(key, secret).Wait();
            });

            await communicator.Start();
            

            return orders;
        }
    }
}
