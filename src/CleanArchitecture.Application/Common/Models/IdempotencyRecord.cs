namespace CleanArchitecture.Application.Common.Models
{
    // Processing state of an Idempotency-Key (API design guide §7.1).
    public enum IdempotencyStatus
    {
        InProgress = 0,
        Completed = 1
    }

    // A captured response, replayed verbatim when a retry presents the same key (§7.1 — a
    // completed key returns "the cached original result"). Deliberately transport-neutral
    // primitives (no ASP.NET types) so this stays an Application-layer contract; the Api layer
    // owns the meaning of these fields (HTTP status / body / Content-Type / Location header).
    public record IdempotencyResponse(int StatusCode, string? ContentType, string? Body, string? Location);

    // One idempotency-key entry: the fingerprint of the request that claimed the key, its
    // processing status, and (once Completed) the response to replay. Null Response while InProgress.
    public record IdempotencyRecord(string Fingerprint, IdempotencyStatus Status, IdempotencyResponse? Response);
}
