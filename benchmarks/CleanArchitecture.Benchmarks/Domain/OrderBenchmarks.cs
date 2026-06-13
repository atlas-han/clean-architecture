using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Benchmarks.Domain
{
    // Order aggregate construction is the most work-heavy domain operation: ctor
    // validation, per-item AddItem invariants, the OrderPlaced domain event, and the
    // TotalAmount roll-up. Scaled across line-item counts to expose O(n) behaviour.
    [MemoryDiagnoser]
    public class OrderBenchmarks
    {
        [Params(1, 5, 25)]
        public int ItemCount { get; set; }

        private List<OrderItem> _items = new List<OrderItem>();

        [GlobalSetup]
        public void Setup()
        {
            _items = new List<OrderItem>(ItemCount);
            for (int i = 0; i < ItemCount; i++)
            {
                _items.Add(new OrderItem(Guid.NewGuid(), "Product " + i, new Money(9.99m), i + 1));
            }
        }

        // Full aggregate build: validation + AddItem loop + domain event + roll-up.
        [Benchmark]
        public Order Create() => new Order("Benchmark Customer", _items);

        // Isolates the TotalAmount LINQ Aggregate over Money for the same item set.
        [Benchmark]
        public Money ComputeTotal() => new Order("Benchmark Customer", _items).TotalAmount;
    }
}
