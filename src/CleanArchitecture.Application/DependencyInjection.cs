using System.Linq;
using System.Reflection;
using CleanArchitecture.Application.Common.Behaviors;
using CleanArchitecture.Application.Common.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            var assembly = Assembly.GetExecutingAssembly();

            services.AddAutoMapper(assembly);
            services.AddValidatorsFromAssembly(assembly);

            services.AddScoped<ISender, Sender>();
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<>), typeof(ValidationBehavior<>));
            RegisterHandlers(services, assembly);

            return services;
        }

        private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
        {
            var handlerInterfaces = new[]
            {
                typeof(IRequestHandler<>),
                typeof(IRequestHandler<,>)
            };

            var handlerTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .SelectMany(t => t.GetInterfaces()
                    .Where(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition()))
                    .Select(i => new { Implementation = t, Service = i }));

            foreach (var entry in handlerTypes)
            {
                services.AddTransient(entry.Service, entry.Implementation);
            }
        }
    }
}
