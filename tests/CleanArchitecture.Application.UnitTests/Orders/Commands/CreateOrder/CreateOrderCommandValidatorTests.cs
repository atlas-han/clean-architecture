using System;
using System.Collections.Generic;
using System.Linq;
using CleanArchitecture.Application.Orders.Commands.CreateOrder;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Commands.CreateOrder
{
    public class CreateOrderCommandValidatorTests
    {
        private readonly CreateOrderCommandValidator _validator = new();

        private static CreateOrderCommand Valid() => new(
            "Alice",
            new List<CreateOrderItemDto> { new(Guid.NewGuid(), 2) });

        [Fact]
        public void Validate_AllValid_IsValid()
        {
            var result = _validator.Validate(Valid());

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_EmptyCustomerName_Fails()
        {
            var command = new CreateOrderCommand("", new List<CreateOrderItemDto> { new(Guid.NewGuid(), 1) });

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOrderCommand.CustomerName));
        }

        [Fact]
        public void Validate_NoItems_Fails()
        {
            var command = new CreateOrderCommand("Alice", new List<CreateOrderItemDto>());

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOrderCommand.Items));
        }

        [Fact]
        public void Validate_ItemWithZeroQuantity_Fails()
        {
            var command = new CreateOrderCommand("Alice", new List<CreateOrderItemDto>
            {
                new(Guid.NewGuid(), 0)
            });

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("Quantity"));
        }

        [Fact]
        public void Validate_ItemWithEmptyProductId_Fails()
        {
            var command = new CreateOrderCommand("Alice", new List<CreateOrderItemDto>
            {
                new(Guid.Empty, 1)
            });

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName.Contains("ProductId"));
        }

        [Fact]
        public void Validate_CustomerNameTooLong_Fails()
        {
            var command = new CreateOrderCommand(
                new string('a', 201),
                new List<CreateOrderItemDto> { new(Guid.NewGuid(), 1) });

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOrderCommand.CustomerName));
        }
    }
}
