using System;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Events;
using CleanArchitecture.Domain.Exceptions;
using CleanArchitecture.Domain.ValueObjects;
using Xunit;

namespace CleanArchitecture.Domain.UnitTests.Entities
{
    public class ProductTests
    {
        [Fact]
        public void Constructor_WithValidArguments_InitializesProperties()
        {
            var product = new Product("Keyboard", "Mechanical", new Money(129000m), 50);

            Assert.NotEqual(Guid.Empty, product.Id);
            Assert.Equal("Keyboard", product.Name);
            Assert.Equal("Mechanical", product.Description);
            Assert.Equal(new Money(129000m), product.Price);
            Assert.Equal(50, product.Stock);
            Assert.Null(product.UpdatedAt);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Constructor_WithEmptyName_ThrowsDomainException(string? name)
        {
            var ex = Assert.Throws<DomainException>(
                () => new Product(name!, "desc", new Money(100m), 10));

            Assert.Contains("must not be empty", ex.Message);
        }

        [Fact]
        public void Constructor_WithNameLongerThanLimit_Throws()
        {
            var longName = new string('a', 201);
            Assert.Throws<DomainException>(
                () => new Product(longName, "desc", new Money(100m), 10));
        }

        [Fact]
        public void Constructor_WithNegativePrice_Throws()
        {
            Assert.Throws<DomainException>(
                () => new Product("name", "desc", new Money(-0.01m), 10));
        }

        [Fact]
        public void Constructor_WithNegativeStock_Throws()
        {
            Assert.Throws<DomainException>(
                () => new Product("name", "desc", new Money(100m), -1));
        }

        [Fact]
        public void ChangePrice_AcceptsZero()
        {
            var product = new Product("name", "desc", new Money(100m), 10);

            product.ChangePrice(Money.Zero);

            Assert.Equal(Money.Zero, product.Price);
        }

        [Fact]
        public void Rename_UpdatesName()
        {
            var product = new Product("Old", "desc", new Money(100m), 10);

            product.Rename("New");

            Assert.Equal("New", product.Name);
        }

        [Fact]
        public void ChangeDescription_TreatsNullAsEmpty()
        {
            var product = new Product("name", "desc", new Money(100m), 10);

            product.ChangeDescription(null!);

            Assert.Equal(string.Empty, product.Description);
        }

        [Fact]
        public void DecreaseStock_WithValidQuantity_ReducesStock()
        {
            var product = new Product("name", "desc", new Money(100m), 10);

            product.DecreaseStock(4);

            Assert.Equal(6, product.Stock);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void DecreaseStock_WithNonPositiveQuantity_Throws(int quantity)
        {
            var product = new Product("name", "desc", new Money(100m), 10);

            Assert.Throws<DomainException>(() => product.DecreaseStock(quantity));
        }

        [Fact]
        public void DecreaseStock_ExceedingAvailableStock_Throws()
        {
            var product = new Product("name", "desc", new Money(100m), 3);

            var ex = Assert.Throws<DomainException>(() => product.DecreaseStock(4));

            Assert.Contains("Insufficient stock", ex.Message);
        }

        [Fact]
        public void MarkCreated_AndMarkUpdated_SetAuditFields()
        {
            var product = new Product("name", "desc", new Money(100m), 10);
            var created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var updated = created.AddHours(1);

            product.MarkCreated(created);
            product.MarkUpdated(updated);

            Assert.Equal(created, product.CreatedAt);
            Assert.Equal(updated, product.UpdatedAt);
        }

        [Fact]
        public void Constructor_RaisesProductRegisteredDomainEvent()
        {
            var product = new Product("Keyboard", "Mechanical", new Money(129000m), 50);

            var registered = Assert.IsType<ProductRegisteredDomainEvent>(Assert.Single(product.DomainEvents));
            Assert.Equal(product.Id, registered.ProductId);
            Assert.Equal("Keyboard", registered.Name);
            Assert.Equal("Mechanical", registered.Description);
            Assert.Equal(129000m, registered.Price);
            Assert.Equal(50, registered.Stock);
        }

        [Fact]
        public void Rename_DoesNotRaiseAdditionalDomainEvent()
        {
            var product = new Product("Old", "desc", new Money(100m), 10);

            product.Rename("New");

            // Only registration (creation) publishes; later mutations are out of outbox scope.
            Assert.IsType<ProductRegisteredDomainEvent>(Assert.Single(product.DomainEvents));
        }
    }
}
