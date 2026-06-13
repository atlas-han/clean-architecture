namespace CleanArchitecture.Infrastructure.Messaging
{
    // One event to publish, as the outbox worker hands it to IEventPublisher. Mirrors the PublishAsync
    // parameters so a whole drain batch can be published in a single pipelined call without leaking
    // OutboxMessage (a persistence type) into the transport seam. IdempotencyKey carries the
    // OutboxMessage.Id; EventType is the OutboxMessage.Type.
    public sealed record EventEnvelope(string EventType, string Key, string Payload, string IdempotencyKey);
}
