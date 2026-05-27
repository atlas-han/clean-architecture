using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Orders.Commands.CreateOrder;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Commands.CreateOrder
{
    public class CreateOrderCommandHandlerTests
    {
        [Fact]
        public async Task Handle_ValidCommand_PersistsOrderAndReturnsId()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("Mouse", "Wireless", 30m, 100);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new CreateOrderCommandHandler(ctx);
            var command = new CreateOrderCommand(
                "Alice",
                new List<CreateOrderItemDto> { new(product.Id, 3) });

            var id = await handler.Handle(command, CancellationToken.None);

            Assert.NotEqual(Guid.Empty, id);

            var persisted = await ctx.Orders.Include(o => o.Items).FirstAsync(o => o.Id == id);
            Assert.Equal("Alice", persisted.CustomerName);
            Assert.Equal(OrderStatus.Pending, persisted.Status);
            Assert.Single(persisted.Items);
            Assert.Equal(90m, persisted.TotalAmount);
        }

        [Fact]
        public async Task Handle_UnknownProductId_ThrowsNotFound()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new CreateOrderCommandHandler(ctx);
            var command = new CreateOrderCommand(
                "Alice",
                new List<CreateOrderItemDto> { new(Guid.NewGuid(), 1) });

            await Assert.ThrowsAsync<NotFoundException>(
                () => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Handle_SnapshotsProductNameAndPrice()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("Keyboard", "Mech", 129m, 10);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new CreateOrderCommandHandler(ctx);
            var command = new CreateOrderCommand(
                "Bob",
                new List<CreateOrderItemDto> { new(product.Id, 2) });

            var id = await handler.Handle(command, CancellationToken.None);

            var persisted = await ctx.Orders.Include(o => o.Items).FirstAsync(o => o.Id == id);
            var line = persisted.Items.Single();
            Assert.Equal("Keyboard", line.ProductName);
            Assert.Equal(129m, line.UnitPrice);
            Assert.Equal(2, line.Quantity);
        }
    }
}
