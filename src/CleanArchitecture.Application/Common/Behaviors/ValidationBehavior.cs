using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Messaging;
using FluentValidation;
using ValidationException = CleanArchitecture.Application.Common.Exceptions.ValidationException;

namespace CleanArchitecture.Application.Common.Behaviors
{
    public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            await ValidationRunner.RunAsync(_validators, request, cancellationToken);
            return await next();
        }
    }

    public class ValidationBehavior<TRequest> : IPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }

        public async Task Handle(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken)
        {
            await ValidationRunner.RunAsync(_validators, request, cancellationToken);
            await next();
        }
    }

    internal static class ValidationRunner
    {
        public static async Task RunAsync<TRequest>(IEnumerable<IValidator<TRequest>> validators, TRequest request, CancellationToken cancellationToken)
        {
            var list = validators.ToList();
            if (list.Count == 0)
                return;

            var context = new ValidationContext<TRequest>(request);

            var results = await Task.WhenAll(
                list.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = results
                .Where(r => r.Errors.Count != 0)
                .SelectMany(r => r.Errors)
                .ToList();

            if (failures.Count != 0)
                throw new ValidationException(failures);
        }
    }
}
