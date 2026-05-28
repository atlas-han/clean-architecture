using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Products.Commands.CreateProduct;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Products.Commands.CreateProduct
{
    public class CreateProductCommandHandlerTests
    {
        [Fact]
        public async Task Handle_PersistsProductAndReturnsId()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new CreateProductCommandHandler(ctx);
            var command = new CreateProductCommand("Mouse", "Wireless", 39000m, 100);

            var id = await handler.Handle(command, CancellationToken.None);

            Assert.NotEqual(Guid.Empty, id);

            var persisted = await ctx.Products.FindAsync(id);
            Assert.NotNull(persisted);
            Assert.Equal("Mouse", persisted!.Name);
            Assert.Equal("Wireless", persisted.Description);
            Assert.Equal(new Money(39000m), persisted.Price);
            Assert.Equal(100, persisted.Stock);
        }
    }
}
