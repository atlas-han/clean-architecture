using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using CleanArchitecture.Application.Orders.Commands.CreateOrder;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Benchmarks.Application
{
    // Write-path handler: looks up the product, decrements stock, builds the Order
    // aggregate, and persists in one SaveChanges. Because it mutates state, each
    // iteration gets a fresh, generously-stocked context so stock never depletes and
    // accumulated orders don't skew measurements across iterations.
    [MemoryDiagnoser]
    public class CreateOrderBenchmarks
    {
        private BenchmarkDbContext _context = null!;
        private CreateOrderCommandHandler _handler = null!;
        private CreateOrderCommand _command = null!;

        [IterationSetup]
        public void IterationSetup()
        {
            _context = BenchmarkDbContext.CreateInMemory();

            var product = new Product("Benchmark Product", "description", new Money(9.99m), 1_000_000);
            _context.Products.Add(product);
            _context.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

            _handler = new CreateOrderCommandHandler(_context);
            _command = new CreateOrderCommand(
                "Benchmark Customer",
                new List<CreateOrderItemDto> { new CreateOrderItemDto(product.Id, 1) });
        }

        [IterationCleanup]
        public void IterationCleanup() => _context.Dispose();

        [Benchmark]
        public Task<Guid> Handle() => _handler.Handle(_command, CancellationToken.None);
    }
}
