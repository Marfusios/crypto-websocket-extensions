using System;
using System.Threading.Tasks;
using Bitmex.Client.Websocket;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Models;
using Crypto.Websocket.Extensions.Orders.Sources;
using Crypto.Websocket.Extensions.Wallets.Sources;
using Serilog;

namespace Crypto.Websocket.Extensions.Sample
{
    public static class OrdersExample
    {
        private static readonly string API_KEY = "";
        private static readonly string API_SECRET = "";

        public static async Task RunEverything()
        {
            var ordBitmex = await StartBitmex(true, HandleOrderChanged, HandleWalletsChanged);

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

        private static void HandleWalletsChanged(CryptoWallet[] wallets)
        {
            foreach (var wallet in wallets)
            {
                HandleWalletChanged(wallet);
            }
        }

        private static void HandleWalletChanged(CryptoWallet wallet)
        {
            Log.Information($"Wallet '{wallet.Type}' " +
                            $"Balance: {wallet.Balance} {wallet.Currency}, " +
                            $"Available: {wallet.BalanceAvailable} {wallet.Currency}, " +
                            $"Pnl: {wallet.RealizedPnl:#.#####}/{wallet.UnrealizedPnl:#.#####}");
        }


        private static async Task<ICryptoOrders> StartBitmex(bool isTestnet, Action<CryptoOrder> handler, 
            Action<CryptoWallet[]> walletHandler)
        {
            var url = isTestnet ? BitmexValues.ApiWebsocketTestnetUrl : BitmexValues.ApiWebsocketUrl;
            var communicator = new BitmexWebsocketCommunicator(url) { Name = "Bitmex" };
            var client = new BitmexWebsocketClient(communicator);

            var source = new BitmexOrderSource(client);
            var orders = new CryptoOrders(source);
            orders.OrderChangedStream.Subscribe(handler);

            var walletSource = new BitmexWalletSource(client);
            walletSource.WalletChangedStream.Subscribe(walletHandler);

            client.Streams.AuthenticationStream.Subscribe(x =>
            {
                Log.Information($"[Bitmex] Authenticated '{x.Success}'");
                client.Send(new Bitmex.Client.Websocket.Requests.WalletSubscribeRequest());
                client.Send(new Bitmex.Client.Websocket.Requests.MarginSubscribeRequest());
                client.Send(new Bitmex.Client.Websocket.Requests.OrderSubscribeRequest());
            });

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                client.Authenticate(API_KEY, API_SECRET);
            });

            await communicator.Start();
            

            return orders;
        }
    }
}
