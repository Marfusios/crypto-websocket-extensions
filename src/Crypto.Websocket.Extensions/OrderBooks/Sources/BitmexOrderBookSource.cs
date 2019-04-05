using System;
using System.Linq;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Responses;
using Bitmex.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.Validations;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class BitmexOrderBookSource : OrderBookLevel2SourceBase
    {
        private BitmexWebsocketClient _client;
        private IDisposable _subscription;


        /// <inheritdoc />
        public BitmexOrderBookSource(BitmexWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitmex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitmexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscription = _client.Streams.BookStream.Subscribe(HandleBookResponse);
        }

        private void HandleBookResponse(BookResponse bookResponse)
        {
            if (bookResponse.Action == BitmexAction.Undefined)
            {
                // weird state, do nothing
                return;
            }

            if (bookResponse.Action == BitmexAction.Partial)
            {
                // received snapshot, convert and stream
                OrderBookSnapshotSubject.OnNext(ConvertLevels(bookResponse.Data));
                return;
            }

            // received difference, convert and stream

            var action = ConvertAction(bookResponse.Action);
            var bulk = new OrderBookLevelBulk(action, ConvertLevels(bookResponse.Data));

            OrderBookSubject.OnNext(bulk);
        }

        private OrderBookLevel[] ConvertLevels(BookLevel[] data)
        {
            return data
                .Select(x => new OrderBookLevel
                (
                    x.Id.ToString(),
                    ConvertSide(x.Side),
                    x.Price,
                    x.Size,
                    null,
                    x.Symbol
                ))
                .ToArray();
        }

        private CryptoSide ConvertSide(BitmexSide side)
        {
            switch (side)
            {
                case BitmexSide.Buy:
                    return CryptoSide.Bid;
                case BitmexSide.Sell:
                    return CryptoSide.Ask;
                default:
                    return CryptoSide.Undefined;
            }
        }

        private OrderBookAction ConvertAction(BitmexAction action)
        {
            switch (action)
            {
                case BitmexAction.Insert:
                    return OrderBookAction.Insert;
                case BitmexAction.Update:
                    return OrderBookAction.Update;
                case BitmexAction.Delete:
                    return OrderBookAction.Delete;
                default:
                    return OrderBookAction.Undefined;
            }
        }
    }
}
