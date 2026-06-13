using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Products.Queries.Dtos;
using CleanArchitecture.Application.Products.Queries.GetProductById;
using CleanArchitecture.Application.Products.Queries.GetProducts;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;

namespace CleanArchitecture.Benchmarks.Application
{
    // Read-path CQRS handlers are idempotent, so the store is seeded once and the
    // handlers are benchmarked repeatedly. SeedCount scales the table to show how the
    // paged query + projection behave as the data set grows.
    [MemoryDiagnoser]
    public class ProductQueryBenchmarks
    {
        [Params(10, 100, 500)]
        public int SeedCount { get; set; }

        private BenchmarkDbContext _context = null!;
        private GetProductsQueryHandler _getProducts = null!;
        private GetProductByIdQueryHandler _getProductById = null!;
        private Guid _existingId;

        [GlobalSetup]
        public void Setup()
        {
            _context = BenchmarkDbContext.CreateInMemory();

            for (int i = 0; i < SeedCount; i++)
            {
                _context.Products.Add(new Product("Product " + i, "description", new Money(10m + i), 100));
            }
            _context.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();

            _existingId = _context.Products.First().Id;
            _getProducts = new GetProductsQueryHandler(_context);
            _getProductById = new GetProductByIdQueryHandler(_context);
        }

        [GlobalCleanup]
        public void Cleanup() => _context.Dispose();

        // Paged read: Count + OrderByDescending + Skip/Take + projection to DTO.
        [Benchmark]
        public Task<PagedResult<ProductDto>> GetProductsPage() =>
            _getProducts.Handle(new GetProductsQuery(1, 20), CancellationToken.None);

        // Single-row read: Where + projection + SingleOrDefault.
        [Benchmark]
        public Task<ProductDto> GetProductById() =>
            _getProductById.Handle(new GetProductByIdQuery(_existingId), CancellationToken.None);
    }
}
