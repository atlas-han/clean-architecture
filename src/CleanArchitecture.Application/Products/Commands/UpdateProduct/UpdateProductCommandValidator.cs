using FluentValidation;
using CleanArchitecture.Application.Common.Validation;

namespace CleanArchitecture.Application.Products.Commands.UpdateProduct
{
    public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
    {
        public UpdateProductCommandValidator()
        {
            RuleFor(x => x.Id).NotEmpty();
            RuleFor(x => x.Name).ProductName();
            RuleFor(x => x.Description).ProductDescription();
            RuleFor(x => x.Price).ProductPrice();
            RuleFor(x => x.Stock).ProductStock();
        }
    }
}
