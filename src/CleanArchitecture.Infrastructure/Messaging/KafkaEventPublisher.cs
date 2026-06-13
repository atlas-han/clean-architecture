using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace CleanArchitecture.Infrastructure.Messaging
{
    // Real Confluent.Kafka producer, used when Kafka:BootstrapServers is configured. Registered as
    // a singleton so the underlying producer (and its connection pool) is shared across worker ticks.
    public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
    {
        // Upper bound on how long a drain batch waits for its delivery reports before the worker gives
        // up on the stragglers and retries them next tick. Bounds the worker's blocking time when the
        // broker is unreachable instead of hanging for the producer's full message.timeout.ms.
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(30);

        private readonly IProducer<string, string> _producer;
        private readonly string _topic;

        public KafkaEventPublisher(string bootstrapServers, string topic)
        {
            var config = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                // Exactly-once-friendly durability: the delivery report only resolves once all in-sync
                // replicas have the record, and the idempotent producer dedupes its own retries on the
                // broker (within a producer session) without reordering.
                Acks = Acks.All,
                EnableIdempotence = true,
                // Idempotence requires max.in.flight <= 5; set it explicitly so the per-partition
                // ordering guarantee is visible at the call site rather than relying on the SDK default.
                MaxInFlight = 5,
                // The idempotent producer stamps sequence numbers, so retries can never duplicate or
                // reorder — retry transient broker blips hard rather than surfacing them to the outbox.
                MessageSendMaxRetries = 10,
                // Throughput levers (the 3000 events/sec path): a short batching window lets many
                // Produce() calls coalesce into one broker request, and compression shrinks the bytes on
                // the wire. Both trade a few ms of latency for much higher sustained throughput.
                LingerMs = 5,
                CompressionType = CompressionType.Lz4
            };

            _producer = new ProducerBuilder<string, string>(config).Build();
            _topic = topic;
        }

        public async Task PublishAsync(string eventType, string key, string payload, string idempotencyKey, CancellationToken cancellationToken)
        {
            // Throws on failed delivery; the worker catches it, records the error, and retries the
            // row next tick (at-least-once).
            await _producer.ProduceAsync(_topic, BuildMessage(eventType, key, payload, idempotencyKey), cancellationToken);
        }

        // Pipelined batch publish: enqueue every message up front (Produce is non-blocking), then a
        // single Flush drives them to the broker and waits for all delivery reports together. Unlike
        // awaiting ProduceAsync per message — which serializes on each broker round-trip — the whole
        // batch is in flight at once, so throughput is bounded by broker/network bandwidth rather than
        // per-message latency. Produce is called in input (drain) order, and with the idempotent
        // producer (max.in.flight <= 5) Kafka preserves that order per partition, so per-aggregate
        // event order still holds. Each delivery report writes its own slot, so one failed message is
        // reported as a failed PublishResult without sinking the rest of the batch.
        public Task<IReadOnlyList<PublishResult>> PublishBatchAsync(IReadOnlyList<EventEnvelope> messages, CancellationToken cancellationToken)
        {
            if (messages.Count == 0)
                return Task.FromResult<IReadOnlyList<PublishResult>>(Array.Empty<PublishResult>());

            var slots = new PublishResult?[messages.Count];

            for (var i = 0; i < messages.Count; i++)
            {
                var index = i;
                EventEnvelope envelope = messages[i];
                try
                {
                    _producer.Produce(
                        _topic,
                        BuildMessage(envelope.EventType, envelope.Key, envelope.Payload, envelope.IdempotencyKey),
                        report => Volatile.Write(
                            ref slots[index],
                            report.Error.IsError
                                ? PublishResult.Failed(report.Error.Reason)
                                : PublishResult.Success()));
                }
                catch (ProduceException<string, string> ex)
                {
                    // Local enqueue failure (e.g. the send queue is full); record it now so the row is
                    // retried next tick.
                    Volatile.Write(ref slots[index], PublishResult.Failed(ex.Error.Reason));
                }
            }

            // Block until every delivery report has fired (bounded). Flush draining a message also
            // dispatches its report callback, so the slot write happens-before Flush returns and the
            // Volatile.Read below observes it — that ordering is the synchronization edge here. The
            // `?? Failed(...)` is the deliberate backstop for the pathological case where a report is
            // never delivered (timeout): such a row degrades to a retry next tick (at-least-once), never
            // a silent "processed". Do not drop the null-coalesce.
            _producer.Flush(FlushTimeout);

            var results = new PublishResult[slots.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                results[i] = Volatile.Read(ref slots[i])
                    ?? PublishResult.Failed("Kafka delivery report not received within the flush timeout");
            }

            return Task.FromResult<IReadOnlyList<PublishResult>>(results);
        }

        private Message<string, string> BuildMessage(string eventType, string key, string payload, string idempotencyKey) =>
            new Message<string, string>
            {
                Key = key,
                Value = payload,
                Headers = new Headers
                {
                    // IdempotencyKey is the OutboxMessage.Id (broker-side dedupe); MessageType is the
                    // OutboxMessage.Type (logical event name).
                    { "IdempotencyKey", Encoding.UTF8.GetBytes(idempotencyKey) },
                    { "MessageType", Encoding.UTF8.GetBytes(eventType) }
                }
            };

        public void Dispose()
        {
            // Flush in-flight deliveries before tearing the producer down on shutdown.
            _producer.Flush(TimeSpan.FromSeconds(5));
            _producer.Dispose();
        }
    }
}
