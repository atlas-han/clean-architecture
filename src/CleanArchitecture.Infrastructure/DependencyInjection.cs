using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.BackgroundServices;
using CleanArchitecture.Infrastructure.Idempotency;
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
                // BeginTransaction/Commit as no-ops, so the BeginTransactionAsync call
                // inside ExecuteInTransactionAsync would otherwise throw — suppress that
                // warning here. The transactional handler's rollback guarantee only
                // holds against a relational provider.
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

            // Maintenance (stop/resume) switch: a shared in-memory singleton seeded from the
            // configured default (Maintenance:Enabled). Toggled at runtime via the
            // /admin/maintenance endpoints, so a maintenance window needs no redeploy. The
            // demo batch worker observes the same singleton and skips its work while stopped.
            // The seed is read lazily from the resolved IConfiguration (not eagerly here) so
            // the final merged configuration wins — indexer + TryParse avoids pulling in the
            // Configuration.Binder package, and a missing/invalid value defaults to false.
            services.AddSingleton<IMaintenanceState>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                bool.TryParse(config["Maintenance:Enabled"], out var enabled);
                return new MaintenanceState(enabled);
            });
            services.AddHostedService<DemoBatchWorker>();

            // Idempotency-Key store (§7.1). Backed by a distributed cache so the dedup guarantee
            // holds across instances: Redis when configured (ConnectionStrings:Redis), else an
            // in-memory distributed cache so dev/test (and the WebApplicationFactory integration
            // tests) run without a Redis server — mirrors the DefaultConnection → InMemory EF
            // fallback above. The store is a stateless wrapper over the singleton cache.
            var redisConnection = configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redisConnection))
            {
                services.AddDistributedMemoryCache();
            }
            else
            {
                services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
            }
            services.AddSingleton<IIdempotencyStore, DistributedCacheIdempotencyStore>();

            // "database" check verifies the EF Core connection (CanConnect) for /health.
            services.AddHealthChecks()
                .AddDbContextCheck<ApplicationDbContext>("database");

            return services;
        }
    }
}
