using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Orders.Commands.CancelOrder;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Enums;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Commands.CancelOrder
{
    public class CancelOrderCommandHandlerTests
    {
        [Fact]
        public async Task Handle_PendingOrder_SetsStatusToCancelled()
        {
            using var ctx = TestDbContextFactory.Create();
            var order = new Order("Alice", new[]
            {
                new OrderItem(Guid.NewGuid(), "Item", new Money(10m), 1)
            });
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new CancelOrderCommandHandler(ctx);

            await handler.Handle(new CancelOrderCommand(order.Id), CancellationToken.None);

            var refreshed = await ctx.Orders.FindAsync(order.Id);
            Assert.NotNull(refreshed);
            Assert.Equal(OrderStatus.Cancelled, refreshed!.Status);
        }

        [Fact]
        public async Task Handle_UnknownOrder_ThrowsNotFound()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new CancelOrderCommandHandler(ctx);

            await Assert.ThrowsAsync<NotFoundException>(
                () => handler.Handle(new CancelOrderCommand(Guid.NewGuid()), CancellationToken.None));
        }
    }
}
