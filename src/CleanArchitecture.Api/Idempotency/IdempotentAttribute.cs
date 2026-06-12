using System;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Api.Idempotency
{
    // Marks a side-effecting action as idempotent (API design guide §7.1). When the request carries
    // an Idempotency-Key header, IdempotencyFilter dedups it — the original response is cached and
    // replayed on retries. As an IFilterFactory the attribute resolves the filter (and its
    // IIdempotencyStore dependency) from DI per action, so the filter needs no global registration.
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class IdempotentAttribute : Attribute, IFilterFactory
    {
        public bool IsReusable => false;

        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            return ActivatorUtilities.CreateInstance<IdempotencyFilter>(serviceProvider);
        }
    }
}
