using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Persistence;
using CleanArchitecture.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // The InMemory provider treats BeginTransaction/Commit as no-ops, so
            // BeginTransactionAsync gives no real atomicity here — it would throw without
            // this suppression. The transactional handler's rollback guarantee only holds
            // against a relational provider (SQL Server, PostgreSQL, etc.).
            services.AddDbContext<ApplicationDbContext>(options =>
                options
                    .UseInMemoryDatabase("CleanArchitectureDb")
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

            services.AddScoped<IApplicationDbContext>(provider =>
                provider.GetRequiredService<ApplicationDbContext>());

            services.AddSingleton<IDateTime, DateTimeService>();

            // "database" check verifies the EF Core connection (CanConnect) for /health.
            services.AddHealthChecks()
                .AddDbContextCheck<ApplicationDbContext>("database");

            return services;
        }
    }
}
