# CleanArchitecture.Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) micro-benchmarks for the hot paths in
the Domain and Application layers. This is a test-tier project (like `tests/`): it may
reference `Application` to measure handler performance and is **not** part of the
`src/` Clean Architecture dependency graph.

## Running

> BenchmarkDotNet shells out to the `dotnet` muxer on your **PATH** to compile its
> generated boilerplate, so a .NET 9 SDK must be the one resolved there — `DOTNET_ROOT`
> alone is not enough. If `dotnet --version` shows an older SDK, prefix the .NET 9
> install: `PATH="$HOME/.dotnet:$PATH" dotnet run -c Release ...`.

```bash
# Full run (all benchmarks, Release is mandatory)
dotnet run -c Release --project benchmarks/CleanArchitecture.Benchmarks

# A single class / pattern
dotnet run -c Release --project benchmarks/CleanArchitecture.Benchmarks -- --filter *Money*

# Fast smoke (one invocation per benchmark — verifies they execute, not their speed)
dotnet run -c Release --project benchmarks/CleanArchitecture.Benchmarks -- --job dry --filter *
```

## What is measured

| Layer | Class | Covers |
|-------|-------|--------|
| Domain | `MoneyBenchmarks` | `Money` construction, `+`, `*` |
| Domain | `OrderBenchmarks` | `Order` aggregate build + `TotalAmount` roll-up (1/5/25 items) |
| Application | `ProductQueryBenchmarks` | `GetProducts` paged read + `GetProductById` (10/100/500 rows) |
| Application | `CreateOrderBenchmarks` | `CreateOrder` write path (lookup → stock decrement → persist) |

Application benchmarks run handlers against an in-memory `IApplicationDbContext`
(`BenchmarkDbContext`) that mirrors the unit-test double, so results reflect handler +
EF Core LINQ cost without a real database.
