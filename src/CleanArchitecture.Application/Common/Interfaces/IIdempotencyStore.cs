using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.Common.Interfaces
{
    // Server-side store for HTTP idempotency keys (API design guide §7.1). A side-effecting request
    // carrying an Idempotency-Key is processed at most once: the first request claims the key
    // (InProgress) and, on success, caches its response (Completed); a retry with the same key
    // replays the cached response instead of re-executing. Backed by a distributed cache (Redis in
    // production) so the guarantee holds across instances — see the Infrastructure implementation.
    public interface IIdempotencyStore
    {
        // The stored record for the key, or null when absent/expired (treated as a new request, §7.1
        // — an expired/pruned key is no longer a conflict).
        Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken);

        // Claims the key for processing (stores InProgress + the request fingerprint). Returns false
        // if a record already exists for the key (a concurrent request claimed it first).
        Task<bool> TryBeginAsync(string key, string fingerprint, CancellationToken cancellationToken);

        // Marks the key Completed and caches the response so later retries replay it.
        Task CompleteAsync(string key, string fingerprint, IdempotencyResponse response, CancellationToken cancellationToken);

        // Releases a claimed-but-unfinished key after a failure/exception so a legitimate retry is not
        // blocked as "in progress" (§7.4 — always release the lock on a non-success outcome).
        Task ReleaseAsync(string key, CancellationToken cancellationToken);
    }
}
