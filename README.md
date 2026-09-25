![Logo](cwe_logo.png)

# Cryptocurrency websocket extensions

[![NuGet version](https://img.shields.io/nuget/v/Crypto.Websocket.Extensions?style=flat-square)](https://www.nuget.org/packages/Crypto.Websocket.Extensions)
[![Nuget downloads](https://img.shields.io/nuget/dt/Crypto.Websocket.Extensions?style=flat-square)](https://www.nuget.org/packages/Crypto.Websocket.Extensions)
[![CI build](https://img.shields.io/github/check-runs/marfusios/crypto-websocket-extensions/master?style=flat-square&label=build)](https://github.com/Marfusios/crypto-websocket-extensions/actions/workflows/dotnet-core.yml)

Shared order book, trade, order, position, and wallet models for cryptocurrency websocket clients.

[Releases and breaking changes](https://github.com/Marfusios/crypto-websocket-extensions/releases)

## License

[Apache License 2.0](LICENSE).

## Features

- installation via NuGet
    - full (with all exchange clients) - [Crypto.Websocket.Extensions](https://www.nuget.org/packages/Crypto.Websocket.Extensions)
    - core (only interfaces and features) - [Crypto.Websocket.Extensions.Core](https://www.nuget.org/packages/Crypto.Websocket.Extensions.Core)
- targets `netstandard2.1`, `net6.0`, `net7.0`, `net8.0`, `net9.0`, `net10.0`
- built on [Websocket.Client 5.5.0](https://www.nuget.org/packages/Websocket.Client/5.5.0) through the updated exchange clients
- third-party exchange adapters for Bybit, Luno, and VALR remain enabled; NuGet resolves the shared websocket transport to the newer package version
- benchmarked order book hot paths with [BenchmarkDotNet](benchmarks/README.md)
- reactive extensions ([Rx.NET](https://github.com/dotnet/reactive))
- integrated logging abstraction ([Microsoft.Extensions.Logging](https://learn.microsoft.com/dotnet/core/extensions/logging))

## Installation

Install the full library, including exchange adapters:

```shell
dotnet add package Crypto.Websocket.Extensions
```

For shared models and order book functionality with your own data source, install `Crypto.Websocket.Extensions.Core` instead.

The packages include a .NET 10 target and retain the earlier targets listed above for compatibility. .NET 6 and .NET 7 are out of support upstream.

## Performance

The order book implementation is tuned for allocation-sensitive websocket streams. Common L2 diff processing avoids temporary notification objects when nobody is subscribed, keeps internal source-to-orderbook handoff on the single-update path, and uses list-based dispatch for bulk level updates to avoid interface enumerator allocations.

The current benchmark suite focuses on `CryptoOrderBook`, `CryptoOrderBookL2`, and related source adapters. In the latest pass, representative BenchmarkDotNet runs showed `CryptoOrderBook.BidLevels` improving from 17,822 ns / 77 KB to 5,409 ns / 4.8 KB, and `CryptoOrderBookL2` diff processing improving from 935 ns / 545 B to 618 ns / 161 B. See the [benchmarks README](benchmarks/README.md) for commands and detailed results.

## Supported exchanges

The full package includes these exchange adapters. Available order book, trade, and account features vary by adapter; see the [adapter sources](src/Crypto.Websocket.Extensions).

| Exchange                   | Websocket client package                                                                    |
| -------------------------- | ------------------------------------------------------------------------------------------- |
| Aster                      | [Aster.Client.Websocket](https://www.nuget.org/packages/Aster.Client.Websocket)             |
| Binance (spot and futures) | [Binance.Client.Websocket](https://www.nuget.org/packages/Binance.Client.Websocket)         |
| Bitfinex                   | [Bitfinex.Client.Websocket](https://www.nuget.org/packages/Bitfinex.Client.Websocket)       |
| BitMEX                     | [Bitmex.Client.Websocket](https://www.nuget.org/packages/Bitmex.Client.Websocket)           |
| Bitstamp                   | [Bitstamp.Client.Websocket](https://www.nuget.org/packages/Bitstamp.Client.Websocket)       |
| Bybit                      | [Bybit.Client.Websocket](https://www.nuget.org/packages/Bybit.Client.Websocket)             |
| Coinbase                   | [Coinbase.Client.Websocket](https://www.nuget.org/packages/Coinbase.Client.Websocket)       |
| Hyperliquid                | [Hyperliquid.Client.Websocket](https://www.nuget.org/packages/Hyperliquid.Client.Websocket) |
| Luno                       | [Luno.Client.Websocket](https://www.nuget.org/packages/Luno.Client.Websocket)               |
| VALR                       | [Valr.Client.Websocket](https://www.nuget.org/packages/Valr.Client.Websocket)               |

The Bitstamp order book adapter currently consumes full snapshots from `order_book_*` streams; `diff_order_book_*` support is not included in 2.17.0.

## Extensions

### Order book

- efficient data structure, based on [howtohft blog post](https://web.archive.org/web/20110219163448/http://howtohft.wordpress.com/2011/02/15/how-to-build-a-fast-limit-order-book/)
- `CryptoOrderBook` - order books supporting L2 and L3 sources
- `CryptoOrderBookL2` - order books specialized for L2 data grouped by price
- support for L2 (grouped by price), L3 (every single order) market data
- support for snapshots and deltas/diffs
- provides streams:
    - `OrderBookUpdatedStream` - streams on an every order book update
    - `BidAskUpdatedStream` - streams when bid or ask price changed (top level of the order book)
    - `TopLevelUpdatedStream` - streams when bid or ask price/amount changed (top level of the order book)
- provides properties and methods:
    - `BidLevels` and `AskLevels` - ordered array of current state of the order book
    - `BidLevelsPerPrice` and `AskLevelsPerPrice` - dictionary of all L3 orders split by price
    - `FindLevelByPrice` and `FindLevelById` - returns specific order book level

Usage (a console application with the full package installed):

```csharp
using System;
using Bitmex.Client.Websocket;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Requests;
using Bitmex.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.OrderBooks.Sources;

const string pair = "XBTUSD";
using var communicator = new BitmexWebsocketCommunicator(BitmexValues.ApiWebsocketUrl);
using var client = new BitmexWebsocketClient(communicator);
using var source = new BitmexOrderBookSource(client);
using var orderBook = new CryptoOrderBook(pair, source);

using var updates = orderBook.OrderBookUpdatedStream.Subscribe(change =>
    Console.WriteLine($"{pair}: {change.Quotes.Bid} / {change.Quotes.Ask}"));

// Subscribe on the initial connection and after every reconnect.
using var reconnects = communicator.ReconnectionHappened.Subscribe(_ =>
    client.Send(new BookSubscribeRequest(pair)));

await communicator.Start();
Console.WriteLine("Press Enter to stop.");
Console.ReadLine();
```

### Trades

- `ITradeSource` - unified trade info stream across all exchanges

### Orders (authenticated)

- `CryptoOrders` class - unified orders status across all exchanges with features:
    - orders view and searching - only executed, search by id, client id, etc.
    - our vs all orders - using client id prefix to distinguish between orders

### Position (authenticated)

- `IPositionSource` - unified position info stream across all exchanges

### Wallet (authenticated)

- `IWalletSource` - unified wallet status stream across all exchanges

---

More usage examples:

- console sample ([link](test_integration/Crypto.Websocket.Extensions.Sample/Program.cs))
- unit tests ([link](test/Crypto.Websocket.Extensions.Tests))
- integration tests ([link](test_integration/Crypto.Websocket.Extensions.Tests.Integration))

**Pull Requests are welcome!**

## Rx.NET

Use `CombineLatest` to observe the latest quotes from several order books. It emits after every source has produced its first value, then whenever any source updates:

```csharp
Observable.CombineLatest(new[]
            {
                bitmexOrderBook.BidAskUpdatedStream,
                bitfinexOrderBook.BidAskUpdatedStream,
                binanceOrderBook.BidAskUpdatedStream,
            })
            .Subscribe(HandleQuoteChanged);

// Method HandleQuoteChanged(IList<IOrderBookChangeInfo> quotes)
// will be called on every exchange's price change
```

## Threading

Rx does not automatically move subscriber work to another thread. Without an explicit scheduler, callbacks run synchronously on the thread that emits the notification. Raw client streams commonly emit from the websocket receive path; order book sources can emit buffered diffs from their background worker. Slow callbacks delay that emitting path.

### Default behavior

For messages emitted sequentially by one websocket client, synchronous callbacks finish before that client emits the next notification. Independent clients and background sources can still emit concurrently.

```csharp
client
    .Streams
    .TradesStream
    .Subscribe(trade => { code1 });

client
    .Streams
    .BookStream
    .Subscribe(book => { code2 });

// 'code1' and 'code2' are called in a correct order, according to websocket flow
// ----- code1 ----- code1 ----- ----- code1
// ----- ----- code2 ----- code2 code2 -----
```

### Parallel subscriptions

`ObserveOn(TaskPoolScheduler.Default)` queues notifications for each subscription on the task pool. Notifications within that subscription remain ordered, while separate subscriptions may execute concurrently. A dedicated thread per subscription is not guaranteed.

```csharp
client
    .Streams
    .TradesStream
    .ObserveOn(TaskPoolScheduler.Default)
    .Subscribe(trade => { code1 });

client
    .Streams
    .BookStream
    .ObserveOn(TaskPoolScheduler.Default)
    .Subscribe(book => { code2 });

// 'code1' and 'code2' may run concurrently; ordering between streams is not guaranteed
// ----- code1 ----- code1 ----- code1 -----
// ----- code2 code2 ----- code2 code2 code2
```

### Parallel subscriptions with synchronization

Use a shared gate to prevent callbacks from overlapping when they access shared state. The lock does not restore the original ordering between independently scheduled streams; see the [Rx.NET implementation of `Synchronize`](https://github.com/dotnet/reactive/blob/main/Rx.NET/Source/src/System.Reactive/Linq/Observable/Synchronize.cs).

```csharp
private static readonly object GATE1 = new object();
client
    .Streams
    .TradesStream
    .ObserveOn(TaskPoolScheduler.Default)
    .Synchronize(GATE1)
    .Subscribe(trade => { code1 });

client
    .Streams
    .BookStream
    .ObserveOn(TaskPoolScheduler.Default)
    .Synchronize(GATE1)
    .Subscribe(book => { code2 });

// 'code1' and 'code2' cannot overlap; ordering between streams is not guaranteed
// ----- code1 ----- code1 ----- ----- code1
// ----- ----- code2 ----- code2 code2 ----
```

## Async/await integration

`Subscribe(async value => ...)` creates an `async void` callback. Rx does not await it, so callbacks can overlap after the first incomplete `await`, and exceptions after that point are not delivered through the observable error channel. For example:

```csharp
client
    .Streams
    .TradesStream
    .Subscribe(async trade => {
        // do smth 1
        await Task.Delay(5000); // waits 5 sec, could be HTTP call or something else
        // do smth 2
    });
```

That `await Task.Delay` won't block stream and subscribe method will be called multiple times concurrently.
If you want to buffer messages and process them one-by-one, then use this:

```csharp
client
    .Streams
    .TradesStream
    .Select(trade => Observable.FromAsync(async () => {
        // do smth 1
        await Task.Delay(5000); // waits 5 sec, could be HTTP call or something else
        // do smth 2
    }))
    .Concat() // executes sequentially
    .Subscribe();
```

If you want to process them concurrently (avoid synchronization), then use this

```csharp
client
    .Streams
    .TradesStream
    .Select(trade => Observable.FromAsync(async () => {
        // do smth 1
        await Task.Delay(5000); // waits 5 sec, could be HTTP call or something else
        // do smth 2
    }))
    .Merge() // executes concurrently
    // .Merge(4) you can limit concurrency with a parameter
    // .Merge(1) is same as .Concat()
    // .Merge(0) is invalid (throws exception)
    .Subscribe();
```

More info on [Github issue](https://github.com/dotnet/reactive/issues/459).

`Concat()` (or `Merge(1)`) waits for each inner operation to complete before subscribing to the next one. Work performed synchronously before the first incomplete `await` still runs on the emitting thread unless you schedule it elsewhere. If messages arrive faster than they can be processed, queued work can grow without bound.

But beware of [producer-consumer problem](https://en.wikipedia.org/wiki/Producer%E2%80%93consumer_problem) when the consumer will be too slow. Here is a [StackOverflow issue](https://stackoverflow.com/questions/11010602/with-rx-how-do-i-ignore-all-except-the-latest-value-when-my-subscribe-method-is)
with an example how to ignore/discard buffered messages and always process only the last one.

## Consulting

I do consulting, please don't hesitate to contact me if you have a custom solution you would like me to implement ([web](http://mkotas.cz/),
<m@mkotas.cz>)

## Publishing

Packages are published from `master` using NuGet Trusted Publishing. Update the shared version in `Directory.Build.props` and add `docs/releases/<version>.md` before releasing. CI builds all package targets, runs Debug and Release unit tests plus recorded-message integration tests on .NET 8 and .NET 10, and packs both libraries with symbols. Live exchange tests are not part of the publishing gate.

Configure the NuGet policy for user `marfusios`, owner `Marfusios`, repository `crypto-websocket-extensions`, workflow `dotnet-core.yml`, and both package IDs. Leave the environment field empty. The workflow uses `NuGet/login` to obtain a temporary API key; no stored NuGet API key is required.

A push to `master` starts the workflow. To trigger it manually from GitHub CLI:

```shell
gh workflow run dotnet-core.yml --ref master
```

CI rejects existing packages from a different commit before publishing. It also verifies each uploaded or skipped package's embedded commit before uploading the next package, then creates the GitHub release from the versioned notes after verifying both public packages. NuGet indexing can take several minutes per package.

For an interrupted publication, rerun the original workflow run instead of dispatching a newer commit with the same version:

```shell
gh run rerun <run-id>
```

Each new package release needs a new version. Documentation-only commits can include `[skip ci]` to avoid starting the publishing workflow for an unchanged version.
