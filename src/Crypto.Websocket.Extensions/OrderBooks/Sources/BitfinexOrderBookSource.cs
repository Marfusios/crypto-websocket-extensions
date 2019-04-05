using System;
using System.Globalization;
using System.Linq;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Responses.Books;
using Crypto.Websocket.Extensions.Models;
using Crypto.Websocket.Extensions.OrderBooks.Models;
using Crypto.Websocket.Extensions.Validations;

namespace Crypto.Websocket.Extensions.OrderBooks.Sources
{
    /// <inheritdoc />
    public class BitfinexOrderBookSource : OrderBookLevel2SourceBase
    {
        private BitfinexWebsocketClient _client;
        private IDisposable _subscription;
        private IDisposable _subscriptionSnapshot;


        /// <inheritdoc />
        public BitfinexOrderBookSource(BitfinexWebsocketClient client)
        {
            ChangeClient(client);
        }

        /// <inheritdoc />
        public override string ExchangeName => "bitfinex";

        /// <summary>
        /// Change client and resubscribe to the new streams
        /// </summary>
        public void ChangeClient(BitfinexWebsocketClient client)
        {
            CryptoValidations.ValidateInput(client, nameof(client));

            _client = client;
            _subscriptionSnapshot?.Dispose();
            _subscription?.Dispose();
            Subscribe();
        }

        private void Subscribe()
        {
            _subscriptionSnapshot = _client.Streams.BookSnapshotStream.Subscribe(HandleSnapshot);
            _subscription = _client.Streams.BookStream.Subscribe(HandleBook);
        }

        private void HandleSnapshot(Book[] books)
        {
            // received snapshot, convert and stream
            OrderBookSnapshotSubject.OnNext(ConvertLevels(books));
        }

        private void HandleBook(Book book)
        {
            var converted = ConvertLevel(book);
            var action = RecognizeAction(book);
            var bulk = new OrderBookLevelBulk(action, new []{converted});
            OrderBookSubject.OnNext(bulk);
        }

        private OrderBookLevel[] ConvertLevels(Book[] data)
        {
            return data
                .Select(ConvertLevel)
                .ToArray();
        }

        private OrderBookLevel ConvertLevel(Book x)
        {
            return new OrderBookLevel
            (
                x.Price.ToString(CultureInfo.InvariantCulture),
                ConvertSide(x.Amount),
                x.Price,
                x.Amount,
                x.Count,
                x.Pair
            );
        }

        private CryptoSide ConvertSide(double amount)
        {
            if (amount > 0)
                return CryptoSide.Bid;
            if (amount < 0)
                return CryptoSide.Ask;
            return CryptoSide.Undefined;
        }

        private OrderBookAction RecognizeAction(Book book)
        {
            if (book.Count > 0)
                return OrderBookAction.Update;
            return OrderBookAction.Delete;
        }
    }
}
