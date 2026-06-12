using System.Threading;
using System.Threading.Tasks;

namespace CleanArchitecture.Infrastructure.Messaging
{
    // Transport seam for the outbox worker. Lets the real Kafka producer be swapped for a logging
    // fallback in Development/Local (mirroring the Redis / SQL fallbacks), and keeps the worker
    // unit-testable without a broker. Not a hosted service — the worker owns the loop.
    public interface IEventPublisher
    {
        Task PublishAsync(string eventType, string key, string payload, CancellationToken cancellationToken);
    }
}
