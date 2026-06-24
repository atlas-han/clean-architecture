using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Extensions;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Common.Extensions
{
    public class DbSetExtensionsTests
    {
        [Fact]
        public async Task FindOrThrowAsync_ExistingEntity_ReturnsIt()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("name", "desc", new Money(100m), 10);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var found = await ctx.Products.FindOrThrowAsync(product.Id, CancellationToken.None);

            Assert.Equal(product.Id, found.Id);
        }

        [Fact]
        public async Task FindOrThrowAsync_MissingEntity_ThrowsNotFoundWithTypeName()
        {
            using var ctx = TestDbContextFactory.Create();

            var ex = await Assert.ThrowsAsync<NotFoundException>(
                () => ctx.Products.FindOrThrowAsync(Guid.NewGuid(), CancellationToken.None));

            Assert.Contains(nameof(Product), ex.Message);
        }
    }
}
