using System;
using System.Linq;
using CleanArchitecture.Application.Products.Commands.UpdateProduct;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Products.Commands.UpdateProduct
{
    public class UpdateProductCommandValidatorTests
    {
        private readonly UpdateProductCommandValidator _validator = new();

        [Fact]
        public void Validate_AllValid_IsValid()
        {
            var command = new UpdateProductCommand(Guid.NewGuid(), "name", "desc", 100m, 10);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_EmptyId_FailsOnId()
        {
            var command = new UpdateProductCommand(Guid.Empty, "name", "desc", 100m, 10);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductCommand.Id));
        }

        [Fact]
        public void Validate_EmptyName_FailsOnName()
        {
            var command = new UpdateProductCommand(Guid.NewGuid(), "", "desc", 100m, 10);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductCommand.Name));
        }

        [Fact]
        public void Validate_TooLongName_FailsOnName()
        {
            var command = new UpdateProductCommand(Guid.NewGuid(), new string('a', 201), "desc", 100m, 10);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductCommand.Name));
        }

        [Fact]
        public void Validate_TooLongDescription_FailsOnDescription()
        {
            var command = new UpdateProductCommand(Guid.NewGuid(), "name", new string('a', 2001), 100m, 10);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductCommand.Description));
        }

        [Fact]
        public void Validate_NegativePrice_FailsOnPrice()
        {
            var command = new UpdateProductCommand(Guid.NewGuid(), "name", "desc", -1m, 10);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductCommand.Price));
        }

        [Fact]
        public void Validate_NegativeStock_FailsOnStock()
        {
            var command = new UpdateProductCommand(Guid.NewGuid(), "name", "desc", 100m, -1);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateProductCommand.Stock));
        }

        [Fact]
        public void Validate_MultipleErrors_ReportsAll()
        {
            var command = new UpdateProductCommand(Guid.Empty, "", "desc", -1m, -1);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            var propertyNames = result.Errors.Select(e => e.PropertyName).ToList();
            Assert.Contains(nameof(UpdateProductCommand.Id), propertyNames);
            Assert.Contains(nameof(UpdateProductCommand.Name), propertyNames);
            Assert.Contains(nameof(UpdateProductCommand.Price), propertyNames);
            Assert.Contains(nameof(UpdateProductCommand.Stock), propertyNames);
        }
    }
}
