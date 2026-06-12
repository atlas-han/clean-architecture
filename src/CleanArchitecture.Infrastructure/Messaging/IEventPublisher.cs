using System.Threading;
using System.Threading.Tasks;

namespace CleanArchitecture.Infrastructure.Messaging
{
    // Transport seam for the outbox worker. Lets the real Kafka producer be swapped for a logging
    // fallback in Development/Local (mirroring the Redis / SQL fallbacks), and keeps the worker
    // unit-testable without a broker. Not a hosted service — the worker owns the loop.
    public interface IEventPublisher
    {
        // idempotencyKey carries the OutboxMessage.Id so the broker side can dedupe; eventType is the
        // OutboxMessage.Type. The Kafka producer surfaces both as message headers (IdempotencyKey /
        // MessageType).
        Task PublishAsync(string eventType, string key, string payload, string idempotencyKey, CancellationToken cancellationToken);
    }
}
