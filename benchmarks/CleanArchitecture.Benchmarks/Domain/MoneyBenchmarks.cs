using BenchmarkDotNet.Attributes;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Benchmarks.Domain
{
    // The Money value object is allocated on every order line and price roll-up,
    // so its construction + operator cost is on the hottest domain path.
    [MemoryDiagnoser]
    public class MoneyBenchmarks
    {
        private readonly Money _a = new Money(19.99m);
        private readonly Money _b = new Money(5.01m);

        [Benchmark]
        public Money Construct() => new Money(123.45m);

        [Benchmark]
        public Money Add() => _a + _b;

        [Benchmark]
        public Money Multiply() => _a * 3;
    }
}
