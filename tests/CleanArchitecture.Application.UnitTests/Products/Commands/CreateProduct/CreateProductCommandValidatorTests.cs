using System.Linq;
using CleanArchitecture.Application.Products.Commands.CreateProduct;
using Xunit;

namespace CleanArchitecture.Application.UnitTests.Products.Commands.CreateProduct
{
    public class CreateProductCommandValidatorTests
    {
        private readonly CreateProductCommandValidator _validator = new();

        [Fact]
        public void Validate_AllValid_IsValid()
        {
            var command = new CreateProductCommand("name", "desc", 100m, 10);

            var result = _validator.Validate(command);

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Validate_EmptyName_FailsOnName()
        {
            var command = new CreateProductCommand("", "desc", 100m, 10);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductCommand.Name));
        }

        [Fact]
        public void Validate_NegativePrice_FailsOnPrice()
        {
            var command = new CreateProductCommand("name", "desc", -1m, 10);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductCommand.Price));
        }

        [Fact]
        public void Validate_NegativeStock_FailsOnStock()
        {
            var command = new CreateProductCommand("name", "desc", 100m, -1);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductCommand.Stock));
        }

        [Fact]
        public void Validate_TooLongName_FailsOnName()
        {
            var command = new CreateProductCommand(new string('a', 201), "desc", 100m, 10);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateProductCommand.Name));
        }

        [Fact]
        public void Validate_MultipleErrors_ReportsAll()
        {
            var command = new CreateProductCommand("", "desc", -1m, -1);

            var result = _validator.Validate(command);

            Assert.False(result.IsValid);
            var propertyNames = result.Errors.Select(e => e.PropertyName).ToList();
            Assert.Contains(nameof(CreateProductCommand.Name), propertyNames);
            Assert.Contains(nameof(CreateProductCommand.Price), propertyNames);
            Assert.Contains(nameof(CreateProductCommand.Stock), propertyNames);
        }
    }
}
