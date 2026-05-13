# Crypto.Websocket.Extensions Benchmarks

This folder contains BenchmarkDotNet benchmarks for allocation-sensitive order book hot paths.

Run all benchmarks:

```powershell
dotnet run --configuration Release --project benchmarks\Crypto.Websocket.Extensions.Benchmarks -- --filter "*"
```

Run the order book processing benchmarks:

```powershell
dotnet run --configuration Release --project benchmarks\Crypto.Websocket.Extensions.Benchmarks -- --filter "*OrderBookProcessingBenchmarks*"
```

Run the order book read/access benchmarks:

```powershell
dotnet run --configuration Release --project benchmarks\Crypto.Websocket.Extensions.Benchmarks -- --filter "*OrderBookReadBenchmarks*"
```

Results are written to `BenchmarkDotNet.Artifacts\results`.

## What The Benchmarks Cover

- `OrderBookProcessingBenchmarks` streams 1,000 prebuilt L2 diffs through `CryptoOrderBookL2` and `CryptoOrderBook`, with and without an `OrderBookUpdatedStream` subscriber.
- `OrderBookReadBenchmarks` measures snapshot-style accessors such as `BidLevels`, `Levels`, and `FindBidLevelByPrice` after a realistic stream of L2 updates has populated the book.

Use the `Allocated` column as the primary signal for GC pressure, and `Mean`/`Ratio` for throughput changes.

## Current Results

Representative results from BenchmarkDotNet ShortRun on Windows, .NET 10.0.5, AMD Ryzen 9 3900X.

### Order Book Processing

| Benchmark | Before | After | Improvement |
| --- | ---: | ---: | ---: |
| `CryptoOrderBookL2`, no observers | 935.4 ns, 545 B | 618.4 ns, 161 B | 34% faster, 70% less allocation |
| `CryptoOrderBookL2`, `OrderBookUpdatedStream` observer | 933.6 ns, 545 B | 683.9 ns, 337 B | 27% faster, 38% less allocation |
| `CryptoOrderBook`, no observers | 1,781.2 ns, 1,154 B | 1,140.0 ns, 770 B | 36% faster, 33% less allocation |
| `CryptoOrderBook`, `OrderBookUpdatedStream` observer | 1,629.1 ns, 1,155 B | 1,238.4 ns, 947 B | 24% faster, 18% less allocation |

The processing improvements mainly come from avoiding notification allocation when no stream has subscribers, handling the common single-diff path directly, using an internal single-bulk handoff from `OrderBookSourceBase` to order books while preserving the public `OrderBookStream` API, lazily materializing single-source notification metadata only when subscribers read `Sources`, dispatching built-in bulk updates through indexed `IReadOnlyList<T>` loops instead of interface enumerators, avoiding repeated dictionary lookups in per-level update/delete paths, skipping temporary collections in buffered/small-batch paths, and caching observable stream wrappers.

### Order Book Reads

| Benchmark | Before | After | Improvement |
| --- | ---: | ---: | ---: |
| `CryptoOrderBook.BidLevels` | 17,822.5 ns, 77,032 B | 5,408.8 ns, 4,832 B | 70% faster, 94% less allocation |
| `CryptoOrderBook.FindBidLevelByPrice` | 37.6 ns, 80 B | 24.3 ns, 0 B | 35% faster, allocation-free |
| `CryptoOrderBook.Levels` | 3,103.4 ns, 9,600 B | 2,964.3 ns, 9,600 B | 4% faster |
| `CryptoOrderBookL2.BidLevels` | 1,721.3 ns, 4,832 B | 1,101.1 ns, 4,832 B | 36% faster |

The read-path improvement for `CryptoOrderBook` comes from removing `OrderedDictionary` value enumerator allocations in grouped L3-compatible level access.
