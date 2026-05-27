using System;
using CleanArchitecture.Application.Orders.Commands.CancelOrder;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Commands.CancelOrder
{
    public class CancelOrderCommandValidatorTests
    {
        private readonly CancelOrderCommandValidator _validator = new();

        [Fact]
        public void Validate_NonEmptyId_IsValid()
        {
            var result = _validator.Validate(new CancelOrderCommand(Guid.NewGuid()));

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_EmptyId_Fails()
        {
            var result = _validator.Validate(new CancelOrderCommand(Guid.Empty));

            Assert.False(result.IsValid);
        }
    }
}
