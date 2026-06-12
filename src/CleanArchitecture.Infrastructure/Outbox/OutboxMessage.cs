using System;

namespace CleanArchitecture.Infrastructure.Outbox
{
    // A pending integration event, written into the same transaction as the originating
    // order/product write (see ConvertDomainEventsToOutboxInterceptor) and later published to
    // Kafka by OutboxProducerWorker. Deliberately not a BaseEntity: it carries its own lifecycle
    // columns (OccurredOnUtc / ProcessedOnUtc / Error) and must not pick up audit-field stamping.
    public class OutboxMessage
    {
        // Version-7 GUID: time-ordered, so the default also gives a stable insert order.
        public Guid Id { get; set; } = Guid.CreateVersion7();

        // The aggregate the event came from; used as the Kafka partition key so events for the
        // same order/product keep their relative order.
        public Guid AggregateId { get; set; }

        // Logical event name (e.g. "OrderPlacedDomainEvent"), surfaced as the Kafka event-type header.
        public string Type { get; set; } = string.Empty;

        // JSON-serialized event payload.
        public string Content { get; set; } = string.Empty;

        public DateTime OccurredOnUtc { get; set; }

        // Null until successfully published; the worker's poll filter keys off this.
        public DateTime? ProcessedOnUtc { get; set; }

        // Last publish failure, if any (left unprocessed for retry).
        public string? Error { get; set; }
    }
}
