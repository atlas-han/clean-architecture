using FluentValidation;
using CleanArchitecture.Application.Common.Validation;

namespace CleanArchitecture.Application.Products.Commands.CreateProduct
{
    public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
    {
        public CreateProductCommandValidator()
        {
            RuleFor(x => x.Name).ProductName();
            RuleFor(x => x.Description).ProductDescription();
            RuleFor(x => x.Price).ProductPrice();
            RuleFor(x => x.Stock).ProductStock();
        }
    }
}
