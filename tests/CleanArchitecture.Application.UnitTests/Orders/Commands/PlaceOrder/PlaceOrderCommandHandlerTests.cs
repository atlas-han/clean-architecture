using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Orders.Commands.PlaceOrder;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Enums;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Commands.PlaceOrder
{
    public class PlaceOrderCommandHandlerTests
    {
        [Fact]
        public async Task Handle_ValidCommand_PersistsOrderAndDecrementsStock()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("Mouse", "Wireless", new Money(30m), 100);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new PlaceOrderCommandHandler(ctx);
            var command = new PlaceOrderCommand(
                "Alice",
                new List<PlaceOrderItemDto> { new(product.Id, 3) });

            var id = await handler.Handle(command, CancellationToken.None);

            Assert.NotEqual(Guid.Empty, id);

            var persisted = await ctx.Orders.Include(o => o.Items).FirstAsync(o => o.Id == id);
            Assert.Equal("Alice", persisted.CustomerName);
            Assert.Equal(OrderStatus.Pending, persisted.Status);
            Assert.Single(persisted.Items);
            Assert.Equal(new Money(90m), persisted.TotalAmount);

            var reloaded = await ctx.Products.FirstAsync(p => p.Id == product.Id);
            Assert.Equal(97, reloaded.Stock);
        }

        [Fact]
        public async Task Handle_UnknownProductId_ThrowsNotFound()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new PlaceOrderCommandHandler(ctx);
            var command = new PlaceOrderCommand(
                "Alice",
                new List<PlaceOrderItemDto> { new(Guid.NewGuid(), 1) });

            await Assert.ThrowsAsync<NotFoundException>(
                () => handler.Handle(command, CancellationToken.None));
        }

        [Fact]
        public async Task Handle_InsufficientStock_ThrowsDomainException()
        {
            using var ctx = TestDbContextFactory.Create();
            var product = new Product("Keyboard", "Mech", new Money(129m), 2);
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new PlaceOrderCommandHandler(ctx);
            var command = new PlaceOrderCommand(
                "Bob",
                new List<PlaceOrderItemDto> { new(product.Id, 5) });

            await Assert.ThrowsAsync<DomainException>(
                () => handler.Handle(command, CancellationToken.None));
        }
    }
}
