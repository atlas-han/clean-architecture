using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ValidationException = CleanArchitecture.Application.Common.Exceptions.ValidationException;

namespace CleanArchitecture.Application.Common.Messaging
{
    public class Sender : ISender
    {
        private readonly IServiceProvider _serviceProvider;

        public Sender(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            await ValidateAsync(request, cancellationToken);

            var requestType = request.GetType();
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            var handler = _serviceProvider.GetRequiredService(handlerType);

            var method = handlerType.GetMethod(nameof(IRequestHandler<IRequest<TResponse>, TResponse>.Handle))!;
            var task = (Task<TResponse>)InvokeHandler(method, handler, request, cancellationToken);
            return await task;
        }

        public async Task Send(IRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null)
                throw new ArgumentNullException(nameof(request));

            await ValidateAsync(request, cancellationToken);

            var requestType = request.GetType();
            var handlerType = typeof(IRequestHandler<>).MakeGenericType(requestType);
            var handler = _serviceProvider.GetRequiredService(handlerType);

            var method = handlerType.GetMethod(nameof(IRequestHandler<IRequest>.Handle))!;
            var task = (Task)InvokeHandler(method, handler, request, cancellationToken);
            await task;
        }

        private static object InvokeHandler(MethodInfo method, object handler, object request, CancellationToken cancellationToken)
        {
            try
            {
                return method.Invoke(handler, new object[] { request, cancellationToken })!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private async Task ValidateAsync(object request, CancellationToken cancellationToken)
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(request.GetType());
            var validators = (IEnumerable<IValidator>)_serviceProvider.GetServices(validatorType);

            var validatorList = validators.ToList();
            if (validatorList.Count == 0)
                return;

            var context = new ValidationContext<object>(request);

            var results = await Task.WhenAll(
                validatorList.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = results
                .Where(r => r.Errors.Any())
                .SelectMany(r => r.Errors)
                .ToList();

            if (failures.Any())
                throw new ValidationException(failures);
        }
    }
}
