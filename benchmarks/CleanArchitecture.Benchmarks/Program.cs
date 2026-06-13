using BenchmarkDotNet.Running;

// Entry point: dispatches to every [Benchmark] class in this assembly.
//
//   dotnet run -c Release --project benchmarks/CleanArchitecture.Benchmarks            # full run, all benchmarks
//   dotnet run -c Release --project benchmarks/CleanArchitecture.Benchmarks -- --filter *Money*
//   dotnet run -c Release --project benchmarks/CleanArchitecture.Benchmarks -- --job dry --filter *   # fast smoke (1 invocation each)
//
// Release is required — BenchmarkDotNet refuses to measure a Debug (unoptimised) build.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
