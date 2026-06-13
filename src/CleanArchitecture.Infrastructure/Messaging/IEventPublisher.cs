using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CleanArchitecture.Infrastructure.Messaging
{
    // Transport seam for the outbox worker. Lets the real Kafka producer be swapped for a logging
    // fallback in Development/Local (mirroring the Redis / SQL fallbacks), and keeps the worker
    // unit-testable without a broker. Not a hosted service — the worker owns the loop.
    public interface IEventPublisher
    {
        // The primitive every transport implements. idempotencyKey carries the OutboxMessage.Id so the
        // broker side can dedupe; eventType is the OutboxMessage.Type. The Kafka producer surfaces both
        // as message headers (IdempotencyKey / MessageType).
        Task PublishAsync(string eventType, string key, string payload, string idempotencyKey, CancellationToken cancellationToken);

        // Publishes a whole drain batch and returns one result per envelope, in the same order, so the
        // worker can mark each row processed or record its error individually. The default
        // implementation walks PublishAsync sequentially — correct, but latency-bound: each message
        // waits for the previous delivery report. The Kafka transport overrides this to enqueue the
        // entire batch and await the delivery reports together; that pipelining is what turns a
        // per-message broker round-trip into batch throughput (the 3000 events/sec path). A single bad
        // message yields a failed PublishResult rather than throwing, so it never sinks the batch.
        async Task<IReadOnlyList<PublishResult>> PublishBatchAsync(IReadOnlyList<EventEnvelope> messages, CancellationToken cancellationToken)
        {
            var results = new PublishResult[messages.Count];
            for (var i = 0; i < messages.Count; i++)
            {
                EventEnvelope envelope = messages[i];
                try
                {
                    await PublishAsync(envelope.EventType, envelope.Key, envelope.Payload, envelope.IdempotencyKey, cancellationToken);
                    results[i] = PublishResult.Success();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // A genuine cancellation aborts the whole pass; surface it to the worker's tick
                    // handler rather than swallowing it into a per-message failure.
                    throw;
                }
                catch (Exception ex)
                {
                    results[i] = PublishResult.Failed(ex.Message);
                }
            }

            return results;
        }
    }
}
