using System;
using CleanArchitecture.Application.Orders.Commands.ConfirmOrder;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Orders.Commands.ConfirmOrder
{
    public class ConfirmOrderCommandValidatorTests
    {
        private readonly ConfirmOrderCommandValidator _validator = new();

        [Fact]
        public void Validate_NonEmptyId_IsValid()
        {
            var result = _validator.Validate(new ConfirmOrderCommand(Guid.NewGuid()));

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_EmptyId_Fails()
        {
            var result = _validator.Validate(new ConfirmOrderCommand(Guid.Empty));

            Assert.False(result.IsValid);
        }
    }
}
