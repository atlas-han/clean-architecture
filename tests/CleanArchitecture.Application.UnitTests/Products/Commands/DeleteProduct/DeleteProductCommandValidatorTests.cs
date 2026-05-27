using System;
using CleanArchitecture.Application.Products.Commands.DeleteProduct;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Products.Commands.DeleteProduct
{
    public class DeleteProductCommandValidatorTests
    {
        private readonly DeleteProductCommandValidator _validator = new();

        [Fact]
        public void Validate_NonEmptyId_IsValid()
        {
            var command = new DeleteProductCommand(Guid.NewGuid());

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_EmptyId_FailsOnId()
        {
            var command = new DeleteProductCommand(Guid.Empty);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(DeleteProductCommand.Id));
        }
    }
}
