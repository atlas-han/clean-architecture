using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Products.Commands.DeleteProduct;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Products.Commands.DeleteProduct
{
    public class DeleteProductCommandHandlerTests
    {
        [Fact]
        public async Task Handle_NonExistentId_ThrowsNotFoundException()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new DeleteProductCommandHandler(ctx);
            var command = new DeleteProductCommand(Guid.NewGuid());

            await Assert.ThrowsAsync<NotFoundException>(
                () => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Handle_ExistingProduct_RemovesFromContext()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("name", "desc", new Money(100m), 10);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new DeleteProductCommandHandler(ctx);
            var command = new DeleteProductCommand(product.Id);

            await handler.Handle(command, CancellationToken.None);

            var deleted = await ctx.Products.FindAsync(product.Id);
            Assert.Null(deleted);
        }
    }
}
