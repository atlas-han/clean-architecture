namespace CleanArchitecture.Infrastructure.Messaging
{
    // Per-message outcome of a batch publish, returned in the same order as the input envelopes so the
    // worker can mark each outbox row processed or record its error individually — a single failed
    // message yields a failed result rather than throwing, so it never sinks the rest of the batch.
    public sealed class PublishResult
    {
        private PublishResult(bool succeeded, string? error)
        {
            Succeeded = succeeded;
            Error = error;
        }

        public bool Succeeded { get; }

        // Null when Succeeded; the broker/transport failure reason otherwise (recorded on the row).
        public string? Error { get; }

        public static PublishResult Success() => new PublishResult(true, null);

        public static PublishResult Failed(string error) => new PublishResult(false, error);
    }
}
