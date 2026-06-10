using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class ProductConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ProductConcurrencyTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task ConcurrentStockDecrements_SecondSaveThrowsConcurrencyException()
        {
            Guid productId;
            using (var seedScope = _factory.Services.CreateScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var product = new Product(
                    "Race-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    "concurrency test",
                    new Money(10m),
                    10);
                db.Products.Add(product);
                await db.SaveChangesAsync(CancellationToken.None);
                productId = product.Id;
            }

            // Two independent contexts load the same product (original Stock = 10).
            using var scope1 = _factory.Services.CreateScope();
            using var scope2 = _factory.Services.CreateScope();
            var db1 = scope1.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var db2 = scope2.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            var p1 = await db1.Products.FirstAsync(p => p.Id == productId);
            var p2 = await db2.Products.FirstAsync(p => p.Id == productId);

            p1.DecreaseStock(3);
            await db1.SaveChangesAsync(CancellationToken.None);

            // The loser still holds original Stock = 10; the concurrency token on
            // Stock must reject this save instead of silently overselling.
            p2.DecreaseStock(5);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => db2.SaveChangesAsync(CancellationToken.None));
        }
    }
}
