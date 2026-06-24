using FluentValidation;

namespace CleanArchitecture.Application.Common.Validation
{
    // Shared FluentValidation rules for the Product create/update command fields, so the two
    // slice validators stay in lockstep and their message text cannot drift apart again.
    // Each slice keeps its own AbstractValidator class (CQRS contract); only the per-field
    // rule definitions are shared via these IRuleBuilder extensions.
    public static class ProductValidationRules
    {
        public static IRuleBuilderOptions<T, string> ProductName<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().WithMessage("Name is required.")
                .MaximumLength(200);

        public static IRuleBuilderOptions<T, string> ProductDescription<T>(this IRuleBuilder<T, string> rule) =>
            rule.MaximumLength(2000);

        public static IRuleBuilderOptions<T, decimal> ProductPrice<T>(this IRuleBuilder<T, decimal> rule) =>
            rule.GreaterThanOrEqualTo(0).WithMessage("Price must be non-negative.");

        public static IRuleBuilderOptions<T, int> ProductStock<T>(this IRuleBuilder<T, int> rule) =>
            rule.GreaterThanOrEqualTo(0).WithMessage("Stock must be non-negative.");
    }
}
