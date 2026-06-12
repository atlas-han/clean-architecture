using System;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.BackgroundServices;
using CleanArchitecture.Infrastructure.Idempotency;
using CleanArchitecture.Infrastructure.Persistence;
using CleanArchitecture.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CleanArchitecture.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
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
            // holds across instances: Redis when configured (ConnectionStrings:Redis). The in-memory
            // distributed cache fallback is allowed only in Development/Local, so dev/test (and the
            // WebApplicationFactory integration tests) run without a Redis server — mirroring the
            // DefaultConnection → InMemory EF fallback above. Outside Development/Local a missing
            // Redis connection string is a configuration error and we fail fast at startup rather
            // than silently degrading to a process-local cache that does not dedup across instances
            // and leaves Redis empty. The store is a stateless wrapper over the singleton cache.
            var redisConnection = configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redisConnection))
            {
                if (!environment.IsDevelopment() && !environment.IsEnvironment("Local"))
                {
                    throw new InvalidOperationException(
                        "ConnectionStrings:Redis is required in the '" + environment.EnvironmentName +
                        "' environment: the Idempotency-Key store must be backed by Redis outside " +
                        "Development/Local. Configure ConnectionStrings:Redis, or run in Development/Local.");
                }

                services.AddDistributedMemoryCache();
            }
            else
            {
                services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
            }

            // Idempotency key lifetime (§7.1), configurable via Idempotency:KeyLifetime (a TimeSpan,
            // e.g. "1.00:00:00" = 24h). Parsed with the indexer + TimeSpan.TryParse to avoid the
            // Configuration.Binder package (same approach as Maintenance:Enabled above); an absent,
            // unparseable, or non-positive value falls back to the 24h default.
            var keyLifetime = TimeSpan.TryParse(configuration["Idempotency:KeyLifetime"], out var parsedLifetime)
                && parsedLifetime > TimeSpan.Zero
                    ? parsedLifetime
                    : TimeSpan.FromHours(24);
            services.AddSingleton<IIdempotencyStore>(provider =>
                new DistributedCacheIdempotencyStore(
                    provider.GetRequiredService<IDistributedCache>(),
                    keyLifetime));

            // /health checks. "database" verifies DB reachability: AddSqlServer pings the real
            // SQL Server (SELECT 1) when DefaultConnection is configured; dev/test fall back to the
            // InMemory provider, where AddDbContextCheck (CanConnect) keeps a green "database" entry
            // without a SQL Server. "redis" pings the configured Redis backend (§7.1) and is only
            // added when ConnectionStrings:Redis is set — dev/test fall back to the in-memory cache
            // and skip it, so /health stays Healthy without a Redis server. Both mirror the
            // SQL Server/InMemory and Redis/in-memory fallbacks chosen above.
            var healthChecks = services.AddHealthChecks();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                healthChecks.AddDbContextCheck<ApplicationDbContext>("database");
            }
            else
            {
                healthChecks.AddSqlServer(connectionString, name: "database");
            }
            if (!string.IsNullOrWhiteSpace(redisConnection))
            {
                healthChecks.AddRedis(redisConnection, name: "redis");
            }

            return services;
        }
    }
}
