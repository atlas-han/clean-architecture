using System;
using System.Collections.Generic;
using CleanArchitecture.Application.Orders.Commands.PlaceOrder;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Commands.PlaceOrder
{
    public class PlaceOrderCommandValidatorTests
    {
        private readonly PlaceOrderCommandValidator _validator = new();

        private static PlaceOrderCommand Valid() => new(
            "Alice",
            new List<PlaceOrderItemDto> { new(Guid.NewGuid(), 2) });

        [Fact]
        public void Validate_AllValid_IsValid()
        {
            var result = _validator.Validate(Valid());

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_EmptyCustomerName_Fails()
        {
            var command = new PlaceOrderCommand("", new List<PlaceOrderItemDto> { new(Guid.NewGuid(), 1) });

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceOrderCommand.CustomerName));
        }

        [Fact]
        public void Validate_NoItems_Fails()
        {
            var command = new PlaceOrderCommand("Alice", new List<PlaceOrderItemDto>());

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(PlaceOrderCommand.Items));
        }

        [Fact]
        public void Validate_ItemWithZeroQuantity_Fails()
        {
            var command = new PlaceOrderCommand("Alice", new List<PlaceOrderItemDto>
            {
                new(Guid.NewGuid(), 0)
            });

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Quantity"));
        }
    }
}
