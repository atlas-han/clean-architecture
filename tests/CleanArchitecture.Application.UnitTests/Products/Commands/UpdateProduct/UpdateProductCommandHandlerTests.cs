using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Products.Commands.UpdateProduct;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Exceptions;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Products.Commands.UpdateProduct
{
    public class UpdateProductCommandHandlerTests
    {
        [Fact]
        public async Task Handle_NonExistentId_ThrowsNotFoundException()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new UpdateProductCommandHandler(ctx);
            var command = new UpdateProductCommand(Guid.NewGuid(), "n", "d", 1m, 1);

            await Assert.ThrowsAsync<NotFoundException>(
                () => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Handle_ExistingProduct_AppliesChanges()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("Old", "od", 100m, 10);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new UpdateProductCommandHandler(ctx);
            var command = new UpdateProductCommand(product.Id, "New", "nd", 200m, 20);

            await handler.Handle(command, CancellationToken.None);

            var updated = await ctx.Products.FindAsync(product.Id);
            Assert.NotNull(updated);
            Assert.Equal("New", updated!.Name);
            Assert.Equal("nd", updated.Description);
            Assert.Equal(200m, updated.Price);
            Assert.Equal(20, updated.Stock);
        }

        [Fact]
        public async Task Handle_InvalidDomainState_PropagatesDomainException()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("name", "desc", 100m, 10);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new UpdateProductCommandHandler(ctx);
            var command = new UpdateProductCommand(product.Id, "name", "desc", -5m, 10);

            await Assert.ThrowsAsync<DomainException>(
                () => handler.Handle(command, CancellationToken.None));
        }
    }
}
