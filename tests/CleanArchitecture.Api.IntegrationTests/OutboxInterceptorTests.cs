using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Events;
using CleanArchitecture.Domain.ValueObjects;
using CleanArchitecture.Infrastructure.Outbox;
using CleanArchitecture.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // Proves ConvertDomainEventsToOutboxInterceptor writes the integration-event row inside the SAME
    // SaveChanges as the order/product that raised it — the no-partial-success guarantee. Runs
    // against the InMemory provider (a fresh database per test) since the interceptor hooks
    // SavingChanges rather than the relational pipeline.
    public class OutboxInterceptorTests
    {
        private sealed class FixedDateTime : IDateTime
        {
            public DateTime UtcNow { get; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        }

        private static ApplicationDbContext NewContext(IDateTime dateTime)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(new ConvertDomainEventsToOutboxInterceptor(dateTime))
                .Options;
            return new ApplicationDbContext(options, dateTime);
        }

        [Fact]
        public async Task SavingOrder_WritesOrderPlacedOutboxRow_InSameSaveChanges()
        {
            var dateTime = new FixedDateTime();
            using var context = NewContext(dateTime);

            var productId = Guid.NewGuid();
            var order = new Order("Alice", new[] { new OrderItem(productId, "Widget", new Money(100m), 2) });
            context.Orders.Add(order);

            await context.SaveChangesAsync(CancellationToken.None);

            var message = Assert.Single(await context.OutboxMessages.ToListAsync());
            Assert.Equal(nameof(OrderPlacedDomainEvent), message.Type);
            Assert.Equal(order.Id, message.AggregateId);
            Assert.Equal(dateTime.UtcNow, message.OccurredOnUtc);
            Assert.Null(message.ProcessedOnUtc);
            Assert.Null(message.Error);

            using var doc = JsonDocument.Parse(message.Content);
            Assert.Equal(order.Id, doc.RootElement.GetProperty("OrderId").GetGuid());
            Assert.Equal("Alice", doc.RootElement.GetProperty("CustomerName").GetString());
            Assert.Equal(200m, doc.RootElement.GetProperty("TotalAmount").GetDecimal());
        }

        [Fact]
        public async Task SavingProduct_WritesProductRegisteredOutboxRow()
        {
            var dateTime = new FixedDateTime();
            using var context = NewContext(dateTime);

            var product = new Product("Keyboard", "Mechanical", new Money(129000m), 50);
            context.Products.Add(product);

            await context.SaveChangesAsync(CancellationToken.None);

            var message = Assert.Single(await context.OutboxMessages.ToListAsync());
            Assert.Equal(nameof(ProductRegisteredDomainEvent), message.Type);
            Assert.Equal(product.Id, message.AggregateId);

            using var doc = JsonDocument.Parse(message.Content);
            Assert.Equal("Keyboard", doc.RootElement.GetProperty("Name").GetString());
            Assert.Equal(129000m, doc.RootElement.GetProperty("Price").GetDecimal());
        }

        [Fact]
        public async Task SavingOrder_ClearsTheEntitysDomainEvents()
        {
            var dateTime = new FixedDateTime();
            using var context = NewContext(dateTime);

            var order = new Order("Bob", new[] { new OrderItem(Guid.NewGuid(), "Widget", new Money(10m), 1) });
            context.Orders.Add(order);
            await context.SaveChangesAsync(CancellationToken.None);

            // Drained into the outbox, so a second SaveChanges can't double-publish them.
            Assert.Empty(order.DomainEvents);
        }

        [Fact]
        public async Task OutboxRow_Id_IsUuidVersion7()
        {
            var dateTime = new FixedDateTime();
            using var context = NewContext(dateTime);

            var order = new Order("Carol", new[] { new OrderItem(Guid.NewGuid(), "Widget", new Money(10m), 1) });
            context.Orders.Add(order);
            await context.SaveChangesAsync(CancellationToken.None);

            // OutboxMessage.Id defaults to Guid.CreateVersion7() — time-ordered, so it doubles as a
            // stable insert order. Guard that the version nibble stays 7 so it can't silently
            // regress to a random (v4) GUID.
            var message = Assert.Single(await context.OutboxMessages.ToListAsync());
            Assert.NotEqual(Guid.Empty, message.Id);
            Assert.Equal(7, message.Id.Version);
        }
    }
}
