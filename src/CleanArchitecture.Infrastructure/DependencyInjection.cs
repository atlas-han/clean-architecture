using System;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.BackgroundServices;
using CleanArchitecture.Infrastructure.Idempotency;
using CleanArchitecture.Infrastructure.Messaging;
using CleanArchitecture.Infrastructure.Outbox;
using CleanArchitecture.Infrastructure.Persistence;
using CleanArchitecture.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

            // Drains each entity's domain events into OutboxMessage rows during SaveChanges, so the
            // event commits in the same transaction as the order/product that raised it (no partial
            // success). Singleton: its only dependency (IDateTime) is a singleton, and EF resolves
            // it once per context build below.
            services.AddSingleton<ConvertDomainEventsToOutboxInterceptor>();

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // No connection string configured → fall back to InMemory so dev/test
                // environments (and the WebApplicationFactory-based integration tests)
                // keep working without a real SQL Server. The InMemory provider treats
                // BeginTransaction/Commit as no-ops, so the BeginTransactionAsync call
                // inside ExecuteInTransactionAsync would otherwise throw — suppress that
                // warning here. The transactional handler's rollback guarantee only
                // holds against a relational provider.
                services.AddDbContext<ApplicationDbContext>((provider, options) =>
                    options
                        .UseInMemoryDatabase("CleanArchitectureDb")
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                        .AddInterceptors(provider.GetRequiredService<ConvertDomainEventsToOutboxInterceptor>()));
            }
            else
            {
                services.AddDbContext<ApplicationDbContext>((provider, options) =>
                    options
                        .UseSqlServer(connectionString)
                        .AddInterceptors(provider.GetRequiredService<ConvertDomainEventsToOutboxInterceptor>()));
            }

            services.AddScoped<IApplicationDbContext>(provider =>
                provider.GetRequiredService<ApplicationDbContext>());

            services.AddSingleton<IDateTime, DateTimeService>();

            // Maintenance (stop/resume) switch: a shared in-memory singleton seeded from the
            // configured default (Maintenance:Enabled). Toggled at runtime via the
            // /admin/maintenance endpoints, so a maintenance window needs no redeploy. The
            // outbox producer worker observes the same singleton and skips its work while stopped.
            // The seed is read lazily from the resolved IConfiguration (not eagerly here) so
            // the final merged configuration wins — indexer + TryParse avoids pulling in the
            // Configuration.Binder package, and a missing/invalid value defaults to false.
            services.AddSingleton<IMaintenanceState>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                bool.TryParse(config["Maintenance:Enabled"], out var enabled);
                return new MaintenanceState(enabled);
            });

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

            // Transactional outbox → Kafka. The request path only writes OutboxMessage rows inside
            // the order/product transaction (ConvertDomainEventsToOutboxInterceptor, wired above);
            // the single OutboxProducerWorker below drains them to Kafka out of band, so request
            // latency and DB load never depend on the broker. The publisher mirrors the Redis/SQL
            // fallback: a real Confluent producer when Kafka:BootstrapServers is set, a logging
            // fallback in Development/Local when it is not, and fail-fast otherwise so production
            // never silently drops events. Ordered after the Redis guard so a fully-unconfigured
            // production startup reports the Redis gap first (matching the existing contract).
            var kafkaBootstrap = configuration["Kafka:BootstrapServers"];
            var configuredTopic = configuration["Kafka:Topic"];
            var kafkaTopic = string.IsNullOrWhiteSpace(configuredTopic)
                ? "clean_architecture_events"
                : configuredTopic;

            if (string.IsNullOrWhiteSpace(kafkaBootstrap))
            {
                if (!environment.IsDevelopment() && !environment.IsEnvironment("Local"))
                {
                    throw new InvalidOperationException(
                        "Kafka:BootstrapServers is required in the '" + environment.EnvironmentName +
                        "' environment: the outbox producer must publish to a real Kafka broker outside " +
                        "Development/Local. Configure Kafka:BootstrapServers, or run in Development/Local.");
                }

                // Dev/Local with no broker configured: register the no-op fallback, but emit a loud
                // WARNING on first resolution so the degraded mode is never silent. Without it the
                // worker logs every row as "published" while nothing reaches Kafka. The factory (over
                // the plain type registration) is what lets us log at the selection site.
                services.AddSingleton<IEventPublisher>(provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<LoggingEventPublisher>>();
                    logger.LogWarning(
                        "Kafka:BootstrapServers is not configured - using the LoggingEventPublisher fallback. " +
                        "Outbox events are logged only and are NOT published to Kafka. Set Kafka:BootstrapServers " +
                        "(e.g. run the Worker in the Local environment) to publish to a real broker.");
                    return new LoggingEventPublisher(logger);
                });
            }
            else
            {
                // Factory (not a pre-built instance) so the container owns the producer's lifetime
                // and disposes it on shutdown, flushing in-flight deliveries.
                services.AddSingleton<IEventPublisher>(_ => new KafkaEventPublisher(kafkaBootstrap, kafkaTopic));
            }

            // Outbox poll cadence / batch size / retry cap (Outbox:PollInterval, Outbox:BatchSize,
            // Outbox:MaxRetries), parsed with the indexer + TryParse to avoid the Configuration.Binder
            // package (same approach as Maintenance:Enabled / Idempotency:KeyLifetime). Absent/invalid
            // values fall back to 5s / 100 / 5. MaxRetries caps publish attempts: once a row has failed
            // that many times the worker dead-letters it instead of retrying forever.
            var pollInterval = TimeSpan.TryParse(configuration["Outbox:PollInterval"], out var parsedPoll)
                && parsedPoll > TimeSpan.Zero
                    ? parsedPoll
                    : TimeSpan.FromSeconds(5);
            var batchSize = int.TryParse(configuration["Outbox:BatchSize"], out var parsedBatch) && parsedBatch > 0
                ? parsedBatch
                : 100;
            var maxRetries = int.TryParse(configuration["Outbox:MaxRetries"], out var parsedMax) && parsedMax > 0
                ? parsedMax
                : 5;

            // Registered as a singleton (so tests can resolve it and drive ProduceBatchAsync directly)
            // but NOT hosted here: running the poll loop is a per-host decision so exactly one process
            // drains the outbox and we never double-publish. The dedicated Worker host opts in via
            // AddOutboxProcessing(); the Api intentionally does not, leaving it to only write rows.
            services.AddSingleton(provider => new OutboxProducerWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<IMaintenanceState>(),
                provider.GetRequiredService<ILogger<OutboxProducerWorker>>(),
                pollInterval,
                batchSize,
                maxRetries));

            return services;
        }

        // Opts a host into actually *running* the outbox producer (the OutboxProducerWorker poll
        // loop). Separated from AddInfrastructure because hosting is a composition-root decision:
        // exactly one process must drain the outbox to Kafka. The CleanArchitecture.Worker host
        // calls this; the Api host does not. Relies on the singleton registered in AddInfrastructure.
        public static IServiceCollection AddOutboxProcessing(this IServiceCollection services)
        {
            services.AddHostedService(provider => provider.GetRequiredService<OutboxProducerWorker>());
            return services;
        }
    }
}
