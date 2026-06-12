using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Products.Queries.GetProductById;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Products.Queries.GetProductById
{
    public class GetProductByIdQueryHandlerTests
    {

        [Fact]
        public async Task Handle_ExistingId_ReturnsDto()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("Item", "desc", new Money(50m), 5);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new GetProductByIdQueryHandler(ctx);

            var dto = await handler.Handle(new GetProductByIdQuery(product.Id), CancellationToken.None);

            Assert.Equal(product.Id, dto.Id);
            Assert.Equal("Item", dto.Name);
            Assert.Equal("desc", dto.Description);
            Assert.Equal(50m, dto.Price);
            Assert.Equal(5, dto.Stock);
        }

        [Fact]
        public async Task Handle_MissingId_ThrowsNotFound()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new GetProductByIdQueryHandler(ctx);

            await Assert.ThrowsAsync<NotFoundException>(
                () => handler.Handle(new GetProductByIdQuery(Guid.NewGuid()), CancellationToken.None));
        }
    }
}
