using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

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
            ArgumentNullException.ThrowIfNull(request);

            var requestType = request.GetType();
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            var handler = _serviceProvider.GetRequiredService(handlerType);
            var handlerMethod = handlerType.GetMethod(nameof(IRequestHandler<IRequest<TResponse>, TResponse>.Handle))!;

            RequestHandlerDelegate<TResponse> pipeline = () =>
                (Task<TResponse>)InvokeMethod(handlerMethod, handler, new object[] { request, cancellationToken });

            var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
            var behaviors = ((IEnumerable<object>)_serviceProvider.GetServices(behaviorType))
                .Reverse()
                .ToList();
            var behaviorMethod = behaviorType.GetMethod(nameof(IPipelineBehavior<IRequest<TResponse>, TResponse>.Handle))!;

            foreach (var behavior in behaviors)
            {
                var next = pipeline;
                var current = behavior;
                pipeline = () =>
                    (Task<TResponse>)InvokeMethod(behaviorMethod, current, new object[] { request, next, cancellationToken });
            }

            return await pipeline();
        }

        public async Task Send(IRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var requestType = request.GetType();
            var handlerType = typeof(IRequestHandler<>).MakeGenericType(requestType);
            var handler = _serviceProvider.GetRequiredService(handlerType);
            var handlerMethod = handlerType.GetMethod(nameof(IRequestHandler<IRequest>.Handle))!;

            RequestHandlerDelegate pipeline = () =>
                (Task)InvokeMethod(handlerMethod, handler, new object[] { request, cancellationToken });

            var behaviorType = typeof(IPipelineBehavior<>).MakeGenericType(requestType);
            var behaviors = ((IEnumerable<object>)_serviceProvider.GetServices(behaviorType))
                .Reverse()
                .ToList();
            var behaviorMethod = behaviorType.GetMethod(nameof(IPipelineBehavior<IRequest>.Handle))!;

            foreach (var behavior in behaviors)
            {
                var next = pipeline;
                var current = behavior;
                pipeline = () =>
                    (Task)InvokeMethod(behaviorMethod, current, new object[] { request, next, cancellationToken });
            }

            await pipeline();
        }

        private static object InvokeMethod(MethodInfo method, object target, object[] args)
        {
            try
            {
                return method.Invoke(target, args)!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
