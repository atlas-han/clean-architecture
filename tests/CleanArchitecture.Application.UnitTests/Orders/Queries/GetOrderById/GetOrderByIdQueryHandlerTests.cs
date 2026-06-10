using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Mappings;
using CleanArchitecture.Application.Orders.Queries.GetOrderById;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Queries.GetOrderById
{
    public class GetOrderByIdQueryHandlerTests
    {
        private readonly IMapper _mapper;

        public GetOrderByIdQueryHandlerTests()
        {
            var config = new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>(), NullLoggerFactory.Instance);
            _mapper = config.CreateMapper();
        }

        [Fact]
        public async Task Handle_ExistingId_ReturnsDtoWithItems()
        {
            using var ctx = TestDbContextFactory.Create();
            var order = new Order("Alice", new[]
            {
                new OrderItem(Guid.NewGuid(), "Mouse", new Money(30m), 2),
                new OrderItem(Guid.NewGuid(), "Pad", new Money(10m), 1)
            });
            ctx.Orders.Add(order);
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new GetOrderByIdQueryHandler(ctx, _mapper);

            var dto = await handler.Handle(new GetOrderByIdQuery(order.Id), CancellationToken.None);

            Assert.Equal(order.Id, dto.Id);
            Assert.Equal("Alice", dto.CustomerName);
            Assert.Equal(2, dto.Items.Count);
            Assert.Equal(70m, dto.TotalAmount); // DTO still exposes decimal
        }

        [Fact]
        public async Task Handle_MissingId_ThrowsNotFound()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new GetOrderByIdQueryHandler(ctx, _mapper);

            await Assert.ThrowsAsync<NotFoundException>(
                () => handler.Handle(new GetOrderByIdQuery(Guid.NewGuid()), CancellationToken.None));
        }
    }
}
