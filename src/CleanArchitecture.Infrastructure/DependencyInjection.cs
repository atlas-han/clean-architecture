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
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // No connection string configured → fall back to InMemory so dev/test
                // environments (and the WebApplicationFactory-based integration tests)
                // keep working without a real SQL Server. The InMemory provider treats
                // BeginTransaction/Commit as no-ops, so BeginTransactionAsync would
                // otherwise throw — suppress that warning here. The transactional
                // handler's rollback guarantee only holds against a relational provider.
                services.AddDbContext<ApplicationDbContext>(options =>
                    options
                        .UseInMemoryDatabase("CleanArchitectureDb")
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            }
            else
            {
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(connectionString));
            }

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
