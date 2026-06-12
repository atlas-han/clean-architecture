using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Products.Queries.GetProducts;
using CleanArchitecture.Application.UnitTests.TestDoubles;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Products.Queries.GetProducts
{
    public class GetProductsQueryHandlerTests
    {

        private static Product MakeProduct(string name)
        {
            return new Product(name, "desc", new Money(10m), 5);
        }

        [Fact]
        public async Task Handle_EmptyStore_ReturnsEmptyPage()
        {
            using var ctx = TestDbContextFactory.Create();
            var handler = new GetProductsQueryHandler(ctx);

            var result = await handler.Handle(new GetProductsQuery(), CancellationToken.None);

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
            ctx.Products.Add(MakeProduct("Older"));
            await ctx.SaveChangesAsync(CancellationToken.None);
            ctx.Products.Add(MakeProduct("Newer"));
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new GetProductsQueryHandler(ctx);

            var result = await handler.Handle(new GetProductsQuery(), CancellationToken.None);

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(2, result.TotalCount);
            Assert.Equal("Newer", result.Items[0].Name);
            Assert.Equal("Older", result.Items[1].Name);
            Assert.True(result.Items[0].CreatedAt > result.Items[1].CreatedAt);
        }

        [Fact]
        public async Task Handle_RespectsPageSize()
        {
            using var ctx = TestDbContextFactory.Create();
            for (var i = 0; i < 5; i++)
            {
                ctx.Products.Add(MakeProduct("P" + i));
                await ctx.SaveChangesAsync(CancellationToken.None);
            }

            var handler = new GetProductsQueryHandler(ctx);

            var result = await handler.Handle(new GetProductsQuery(Page: 1, PageSize: 2), CancellationToken.None);

            Assert.Equal(2, result.Items.Count);
            Assert.Equal(5, result.TotalCount);
            Assert.Equal(1, result.Page);
            Assert.Equal(2, result.PageSize);
            Assert.Equal(3, result.TotalPages);
            Assert.True(result.HasNext);
            Assert.False(result.HasPrevious);
            Assert.Equal("P4", result.Items[0].Name);
            Assert.Equal("P3", result.Items[1].Name);
        }

        [Fact]
        public async Task Handle_LastPage_ReturnsRemainder()
        {
            using var ctx = TestDbContextFactory.Create();
            for (var i = 0; i < 5; i++)
            {
                ctx.Products.Add(MakeProduct("P" + i));
                await ctx.SaveChangesAsync(CancellationToken.None);
            }

            var handler = new GetProductsQueryHandler(ctx);

            var result = await handler.Handle(new GetProductsQuery(Page: 3, PageSize: 2), CancellationToken.None);

            var dto = Assert.Single(result.Items);
            Assert.Equal("P0", dto.Name);
            Assert.Equal(3, result.Page);
            Assert.True(result.HasPrevious);
            Assert.False(result.HasNext);
        }

        [Fact]
        public async Task Handle_PageZero_IsClampedToFirstPage()
        {
            using var ctx = TestDbContextFactory.Create();
            ctx.Products.Add(MakeProduct("Only"));
            await ctx.SaveChangesAsync(CancellationToken.None);

            var handler = new GetProductsQueryHandler(ctx);

            var result = await handler.Handle(new GetProductsQuery(Page: 0, PageSize: 10), CancellationToken.None);

            Assert.Single(result.Items);
            Assert.Equal(1, result.Page);
        }
    }
}
