using System;
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
        private readonly IProducer<string, string> _producer;
        private readonly string _topic;

        public KafkaEventPublisher(string bootstrapServers, string topic)
        {
            var config = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                // At-least-once durability: the delivery report only resolves once all in-sync
                // replicas have the record, and idempotence dedupes producer retries on the broker.
                Acks = Acks.All,
                EnableIdempotence = true
            };

            _producer = new ProducerBuilder<string, string>(config).Build();
            _topic = topic;
        }

        public async Task PublishAsync(string eventType, string key, string payload, string idempotencyKey, CancellationToken cancellationToken)
        {
            var message = new Message<string, string>
            {
                Key = key,
                Value = payload,
                Headers = new Headers
                {
                    // Payload is JSON-serialized; Idempotency-Key is the OutboxMessage.Id (broker-side
                    // dedupe); MessageType is the OutboxMessage.Type (logical event name).
                    { "Type", Encoding.UTF8.GetBytes("json") },
                    { "Idempotency-Key", Encoding.UTF8.GetBytes(idempotencyKey) },
                    { "MessageType", Encoding.UTF8.GetBytes(eventType) }
                }
            };

            // Throws on failed delivery; the worker catches it, records the error, and retries the
            // row next tick (at-least-once).
            await _producer.ProduceAsync(_topic, message, cancellationToken);
        }

        public void Dispose()
        {
            // Flush in-flight deliveries before tearing the producer down on shutdown.
            _producer.Flush(TimeSpan.FromSeconds(5));
            _producer.Dispose();
        }
    }
}
