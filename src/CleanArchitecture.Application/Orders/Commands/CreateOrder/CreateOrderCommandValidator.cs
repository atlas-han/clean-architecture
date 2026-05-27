using FluentValidation;

namespace CleanArchitecture.Application.Orders.Commands.CreateOrder
{
    public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
    {
        public CreateOrderCommandValidator()
        {
            RuleFor(x => x.CustomerName)
                .NotEmpty().WithMessage("CustomerName is required.")
                .MaximumLength(200).WithMessage("CustomerName must be at most 200 characters.");

            RuleFor(x => x.Items)
                .NotEmpty().WithMessage("At least one item is required.");

            RuleForEach(x => x.Items).ChildRules(item =>
            {
                item.RuleFor(i => i.ProductId)
                    .NotEmpty().WithMessage("ProductId is required.");
                item.RuleFor(i => i.Quantity)
                    .GreaterThan(0).WithMessage("Quantity must be positive.");
            });
        }
    }
}
