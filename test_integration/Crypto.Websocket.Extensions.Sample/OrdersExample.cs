using Binance.Client.Websocket;
using Binance.Client.Websocket.Client;
using Binance.Client.Websocket.Signing;
using Binance.Client.Websocket.Websockets;
using Bitmex.Client.Websocket;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.Core.Orders;
using Crypto.Websocket.Extensions.Core.Orders.Models;
using Crypto.Websocket.Extensions.Core.Positions.Models;
using Crypto.Websocket.Extensions.Core.Wallets.Models;
using Crypto.Websocket.Extensions.Orders.Sources;
using Crypto.Websocket.Extensions.Positions.Sources;
using Crypto.Websocket.Extensions.Wallets.Sources;
using Hyperliquid.Client.Websocket;
using Hyperliquid.Client.Websocket.Client;
using Hyperliquid.Client.Websocket.Websockets;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Threading.Tasks;
using Hyperliquid.Client.Websocket.Requests.Subscriptions;

namespace Crypto.Websocket.Extensions.Sample
{
    public static class OrdersExample
    {
        private const string ApiKey = "0x0366477c01Ef7362E8bC0C2d33Df5B3a0A6342b9";
        private const string ApiSecret = "";

        public static async Task RunEverything()
        {
            //var ordBitmex = await StartBitmex(false, HandleOrderChanged, HandleWalletsChanged, HandlePositionsChanged);
            //var ordBinance = await StartBinance(HandleOrderChanged);
            var ordHyperliquid = await StartHyperliquid(HandleOrderChanged);

            Log.Information("Waiting for orders...");
        }

        private static void HandleOrderChanged(CryptoOrder order)
        {
            Log.Information($"Order '{order.ClientId}' [{order.Pair} {order.Side} {order.Type}] changed. " +
                            $"Price: {order.PriceGrouped}, Amount: {order.AmountOrig:#.#####}/{order.AmountOrigQuote}, " +
                            $"cumulative: {order.AmountFilledCumulative:#.#####}/{order.AmountFilledCumulativeQuote}, " +
                            $"filled: {order.AmountFilled:#.#####}/{order.AmountFilledQuote}, " +
                            $"Status: {order.OrderStatus} ({order.OrderStatusRaw})");
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

        private static void HandlePositionsChanged(CryptoPosition[] positions)
        {
            foreach (var pos in positions)
            {
                HandlePositionChanged(pos);
            }
        }

        private static void HandlePositionChanged(CryptoPosition pos)
        {
            Log.Information($"Position '{pos.Pair}' [{pos.Side}], " +
                            $"price: {pos.LastPrice:0.00######}, amount: {pos.Amount}/{pos.AmountQuote}, " +
                            $"leverage: {pos.Leverage}x, " +
                            $"pnl realized: {pos.RealizedPnl:0.00######}, unrealized: {pos.UnrealizedPnl:0.00######}, " +
                            $"liquidation: {pos.LiquidationPrice:0.00######}");
        }


        private static async Task<ICryptoOrders> StartBitmex(bool isTestnet, Action<CryptoOrder> handler,
            Action<CryptoWallet[]> walletHandler, Action<CryptoPosition[]> positionHandler)
        {
            var url = isTestnet ? BitmexValues.ApiWebsocketTestnetUrl : BitmexValues.ApiWebsocketUrl;
            var communicator = new BitmexWebsocketCommunicator(url, Program.Logger.CreateLogger<BitmexWebsocketCommunicator>()) { Name = "Bitmex" };
            var client = new BitmexWebsocketClient(communicator, Program.Logger.CreateLogger<BitmexWebsocketClient>());

            var source = new BitmexOrderSource(client);
            var orders = new CryptoOrders(source);
            orders.OrderChangedStream.Subscribe(handler);

            var walletSource = new BitmexWalletSource(client);
            walletSource.WalletChangedStream.Subscribe(walletHandler);

            var positionSource = new BitmexPositionSource(client);
            positionSource.PositionsStream.Subscribe(positionHandler);

            client.Streams.AuthenticationStream.Subscribe(x =>
            {
                Log.Information($"[Bitmex] Authenticated '{x.Success}'");
                client.Send(new Bitmex.Client.Websocket.Requests.WalletSubscribeRequest());
                client.Send(new Bitmex.Client.Websocket.Requests.MarginSubscribeRequest());
                client.Send(new Bitmex.Client.Websocket.Requests.PositionSubscribeRequest());
                client.Send(new Bitmex.Client.Websocket.Requests.OrderSubscribeRequest());
            });

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                Log.Information("[Bitmex] Reconnected, type: {type}", x.Type);
                client.Authenticate(ApiKey, ApiSecret);
            });

            await communicator.Start();


            return orders;
        }

        private static async Task<ICryptoOrders> StartBinance(Action<CryptoOrder> handler)
        {
            var url = BinanceValues.ApiWebsocketUrl;
            var communicator = new BinanceWebsocketCommunicator(url, Program.Logger.CreateLogger<BinanceWebsocketCommunicator>()) { Name = "Binance" };
            var client = new BinanceWebsocketClient(communicator, Program.Logger.CreateLogger<BinanceWebsocketClient>());

            var source = new BinanceOrderSource(client);
            var orders = new CryptoOrders(source);
            orders.OrderChangedStream.Subscribe(handler);

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                Log.Information("[Binance] Reconnected, type: {type}", x.Type);
            });

            await communicator.Authenticate(ApiKey, new BinanceHmac(ApiSecret));
            await communicator.Start();

            return orders;
        }

        private static async Task<ICryptoOrders> StartHyperliquid(Action<CryptoOrder> handler)
        {
            var url = HyperliquidValues.MainnetWebsocketApiUrl;
            var communicator = new HyperliquidWebsocketCommunicator(url, Program.Logger.CreateLogger<HyperliquidWebsocketCommunicator>()) { Name = "Hyperliquid" };
            var client = new HyperliquidWebsocketClient(communicator, Program.Logger.CreateLogger<BinanceWebsocketClient>());

            var source = new HyperliquidOrderSource(client);
            var orders = new CryptoOrders(source);
            orders.OrderChangedStream.Subscribe(handler);

            communicator.ReconnectionHappened.Subscribe(x =>
            {
                Log.Information("[Hyperliquid] Reconnected, type: {type}", x.Type);

                client.Send(new UserOrderUpdatesSubscribeRequest(ApiKey));
                client.Send(new UserFillsSubscribeRequest(ApiKey));
            });

            await communicator.Start();

            return orders;
        }
    }
}
