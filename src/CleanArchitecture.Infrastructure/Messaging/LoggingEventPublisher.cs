using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.Messaging
{
    // Dev/test fallback used when Kafka:BootstrapServers is not configured (Development/Local only;
    // production fails fast in AddInfrastructure). Mirrors the in-memory cache / InMemory EF
    // fallbacks: the outbox + worker run end to end without a broker, logging what would have been
    // published instead of producing to Kafka.
    public sealed class LoggingEventPublisher : IEventPublisher
    {
        private readonly ILogger<LoggingEventPublisher> _logger;

        public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
        {
            _logger = logger;
        }

        public Task PublishAsync(string eventType, string key, string payload, string idempotencyKey, CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Outbox event published (logging fallback): type={event_type} key={event_key} idempotency_key={idempotency_key} payload={event_payload}",
                eventType, key, idempotencyKey, payload);

            return Task.CompletedTask;
        }
    }
}
