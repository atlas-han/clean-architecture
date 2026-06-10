using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Application.Orders.Queries.GetOrders;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Queries.GetOrders
{
    public class GetOrdersQueryHandlerTests
    {
        private readonly IMapper _mapper;

        public GetOrdersQueryHandlerTests()
        {
            var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance);
            _mapper = config.CreateMapper();
        }

        private static Order MakeOrder(string customer)
        {
            return new Order(customer, new[]
            {
                new OrderItem(Guid.NewGuid(), "Item", new Money(10m), 1)
            });
        }

        [Fact]
        public async Task Handle_EmptyStore_ReturnsEmptyPage()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new GetOrdersQueryHandler(ctx, _mapper);

            var result = await handler.Handle(new GetOrdersQuery(), CancellationToken.None);

            Assert.Empty(result.Items);
            Assert.Equal(0, result.TotalCount);
            Assert.Equal(0, result.TotalPages);
            Assert.False(result.HasPrevious);
            Assert.False(result.HasNext);
        }

        [Fact]
        public async Task Handle_ReturnsItemsOrderedByCreatedAtDescending()
        {
            using var ctx = TestDbContextFactory.Create();
            ctx.Orders.Add(MakeOrder("Older"));
            await ctx.SaveChangesAsync(CancellationToken.None);
            ctx.Orders.Add(MakeOrder("Newer"));
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new GetOrdersQueryHandler(ctx, _mapper);

            var result = await handler.Handle(new GetOrdersQuery(), CancellationToken.None);

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(2, result.TotalCount);
            Assert.Equal("Newer", result.Items[0].CustomerName);
            Assert.Equal("Older", result.Items[1].CustomerName);
            Assert.True(result.Items[0].CreatedAt > result.Items[1].CreatedAt);
        }

        [Fact]
        public async Task Handle_MapsItemsAndTotalAmount()
        {
            using var ctx = TestDbContextFactory.Create();
            var order = new Order("Alice", new[]
            {
                new OrderItem(Guid.NewGuid(), "A", new Money(50m), 2),
                new OrderItem(Guid.NewGuid(), "B", new Money(10m), 3)
            });
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new GetOrdersQueryHandler(ctx, _mapper);

            var result = await handler.Handle(new GetOrdersQuery(), CancellationToken.None);

            var dto = Assert.Single(result.Items);
            Assert.Equal(2, dto.Items.Count);
            Assert.Equal(130m, dto.TotalAmount);
        }

        [Fact]
        public async Task Handle_PageZero_IsClampedToFirstPage()
        {
            using var ctx = TestDbContextFactory.Create();
            ctx.Orders.Add(MakeOrder("Only"));
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new GetOrdersQueryHandler(ctx, _mapper);

            var result = await handler.Handle(new GetOrdersQuery(Page: 0, PageSize: 10), CancellationToken.None);

            Assert.Single(result.Items);
            Assert.Equal(1, result.Page);
        }

        [Fact]
        public async Task Handle_PageSizeOverMax_IsClampedToHundred()
        {
            using var ctx = TestDbContextFactory.Create();
            for (var i = 0; i < 5; i++)
            {
                ctx.Orders.Add(MakeOrder("C" + i));
                await ctx.SaveChangesAsync(CancellationToken.None);
            }

            var handler = new GetOrdersQueryHandler(ctx, _mapper);

            var result = await handler.Handle(new GetOrdersQuery(Page: 1, PageSize: 500), CancellationToken.None);

            Assert.Equal(5, result.Items.Count);
            Assert.Equal(100, result.PageSize);
        }

        [Fact]
        public async Task Handle_RespectsPageSize()
        {
            using var ctx = TestDbContextFactory.Create();
            for (var i = 0; i < 5; i++)
            {
                ctx.Orders.Add(MakeOrder("C" + i));
                await ctx.SaveChangesAsync(CancellationToken.None);
            }

            var handler = new GetOrdersQueryHandler(ctx, _mapper);

            var result = await handler.Handle(new GetOrdersQuery(Page: 1, PageSize: 2), CancellationToken.None);

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(3, result.TotalPages);
            Assert.True(result.HasNext);
            Assert.False(result.HasPrevious);
            Assert.Equal("C4", result.Items[0].CustomerName);
            Assert.Equal("C3", result.Items[1].CustomerName);
        }
    }
}
