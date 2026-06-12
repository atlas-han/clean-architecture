using System;
using System.Collections.Generic;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Enums;
using CleanArchitecture.Domain.Events;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Domain.UnitTests.Entities
{
    public class OrderTests
    {
        private static OrderItem Item(decimal price = 100m, int qty = 2) =>
            new OrderItem(Guid.NewGuid(), "Sample", new Money(price), qty);

        [Fact]
        public void Constructor_WithValidArguments_InitializesProperties()
        {
            var items = new List<OrderItem> { Item(100m, 2), Item(50m, 1) };

            var order = new Order("Alice", items);

            Assert.NotEqual(Guid.Empty, order.Id);
            Assert.Equal("Alice", order.CustomerName);
            Assert.Equal(OrderStatus.Pending, order.Status);
            Assert.Equal(2, order.Items.Count);
            Assert.Equal(new Money(250m), order.TotalAmount);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Constructor_WithEmptyCustomerName_Throws(string? name)
        {
            var ex = Assert.Throws<DomainException>(
                () => new Order(name!, new[] { Item() }));

            Assert.Contains("must not be empty", ex.Message);
        }

        [Fact]
        public void Constructor_WithCustomerNameTooLong_Throws()
        {
            var longName = new string('a', 201);
            Assert.Throws<DomainException>(
                () => new Order(longName, new[] { Item() }));
        }

        [Fact]
        public void Constructor_WithNullItems_Throws()
        {
            Assert.Throws<DomainException>(
                () => new Order("Alice", null!));
        }

        [Fact]
        public void Constructor_WithEmptyItems_Throws()
        {
            var ex = Assert.Throws<DomainException>(
                () => new Order("Alice", new List<OrderItem>()));

            Assert.Contains("at least one item", ex.Message);
        }

        [Fact]
        public void OrderItem_WithEmptyProductId_Throws()
        {
            Assert.Throws<DomainException>(
                () => new OrderItem(Guid.Empty, "Product", new Money(10m), 1));
        }

        [Fact]
        public void OrderItem_WithZeroQuantity_Throws()
        {
            Assert.Throws<DomainException>(
                () => new OrderItem(Guid.NewGuid(), "Product", new Money(10m), 0));
        }

        [Fact]
        public void OrderItem_WithNullUnitPrice_Throws()
        {
            Assert.Throws<DomainException>(
                () => new OrderItem(Guid.NewGuid(), "Product", null!, 1));
        }

        [Fact]
        public void OrderItem_LineTotal_IsUnitPriceTimesQuantity()
        {
            var item = new OrderItem(Guid.NewGuid(), "Product", new Money(12.5m), 4);

            Assert.Equal(new Money(50m), item.LineTotal);
        }

        [Fact]
        public void Cancel_PendingOrder_SetsStatusToCancelled()
        {
            var order = new Order("Alice", new[] { Item() });

            order.Cancel();

            Assert.Equal(OrderStatus.Cancelled, order.Status);
        }

        [Fact]
        public void Cancel_AlreadyCancelled_Throws()
        {
            var order = new Order("Alice", new[] { Item() });
            order.Cancel();

            Assert.Throws<DomainException>(() => order.Cancel());
        }

        [Fact]
        public void Confirm_PendingOrder_SetsStatusToConfirmed()
        {
            var order = new Order("Alice", new[] { Item() });

            order.Confirm();

            Assert.Equal(OrderStatus.Confirmed, order.Status);
        }

        [Fact]
        public void Confirm_CancelledOrder_Throws()
        {
            var order = new Order("Alice", new[] { Item() });
            order.Cancel();

            Assert.Throws<DomainException>(() => order.Confirm());
        }

        [Fact]
        public void AddItem_ToCancelledOrder_Throws()
        {
            var order = new Order("Alice", new[] { Item() });
            order.Cancel();

            Assert.Throws<DomainException>(
                () => order.AddItem(Item()));
        }

        [Fact]
        public void AddItem_ToConfirmedOrder_Throws()
        {
            var order = new Order("Alice", new[] { Item() });
            order.Confirm();

            Assert.Throws<DomainException>(
                () => order.AddItem(Item()));
        }

        [Fact]
        public void Constructor_RaisesOrderPlacedDomainEvent()
        {
            var productId = Guid.NewGuid();
            var items = new List<OrderItem> { new OrderItem(productId, "Sample", new Money(100m), 2) };

            var order = new Order("Alice", items);

            var placed = Assert.IsType<OrderPlacedDomainEvent>(Assert.Single(order.DomainEvents));
            Assert.Equal(order.Id, placed.OrderId);
            Assert.Equal("Alice", placed.CustomerName);
            Assert.Equal(200m, placed.TotalAmount);
            var line = Assert.Single(placed.Items);
            Assert.Equal(productId, line.ProductId);
            Assert.Equal("Sample", line.ProductName);
            Assert.Equal(100m, line.UnitPrice);
            Assert.Equal(2, line.Quantity);
        }

        [Fact]
        public void Cancel_DoesNotRaiseAdditionalDomainEvent()
        {
            var order = new Order("Alice", new[] { Item() });

            order.Cancel();

            // Only creation publishes; lifecycle transitions are intentionally out of outbox scope.
            Assert.IsType<OrderPlacedDomainEvent>(Assert.Single(order.DomainEvents));
        }

        [Fact]
        public void ClearDomainEvents_RemovesPendingEvents()
        {
            var order = new Order("Alice", new[] { Item() });

            order.ClearDomainEvents();

            Assert.Empty(order.DomainEvents);
        }
    }
}
