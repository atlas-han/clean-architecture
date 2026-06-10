using System;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Orders.Commands.ConfirmOrder;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Enums;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Commands.ConfirmOrder
{
    public class ConfirmOrderCommandHandlerTests
    {
        [Fact]
        public async Task Handle_PendingOrder_SetsStatusToConfirmed()
        {
            using var ctx = TestDbContextFactory.Create();
            var order = new Order("Alice", new[]
            {
                new OrderItem(Guid.NewGuid(), "Item", new Money(10m), 1)
            });
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new ConfirmOrderCommandHandler(ctx);

            await handler.Handle(new ConfirmOrderCommand(order.Id), CancellationToken.None);

            var refreshed = await ctx.Orders.FindAsync(order.Id);
            Assert.NotNull(refreshed);
            Assert.Equal(OrderStatus.Confirmed, refreshed!.Status);
        }

        [Fact]
        public async Task Handle_UnknownOrder_ThrowsNotFound()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new ConfirmOrderCommandHandler(ctx);

            await Assert.ThrowsAsync<NotFoundException>(
                () => handler.Handle(new ConfirmOrderCommand(Guid.NewGuid()), CancellationToken.None));
        }

        [Fact]
        public async Task Handle_CancelledOrder_ThrowsDomainException()
        {
            using var ctx = TestDbContextFactory.Create();
            var order = new Order("Bob", new[]
            {
                new OrderItem(Guid.NewGuid(), "Item", new Money(10m), 1)
            });
            order.Cancel();
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new ConfirmOrderCommandHandler(ctx);

            await Assert.ThrowsAsync<DomainException>(
                () => handler.Handle(new ConfirmOrderCommand(order.Id), CancellationToken.None));
        }
    }
}
