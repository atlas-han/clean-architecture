using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using Microsoft.Extensions.Caching.Distributed;

namespace CleanArchitecture.Infrastructure.Idempotency
{
    // IIdempotencyStore backed by IDistributedCache (API design guide §7.1 — "Redis 또는 DB에 저장").
    // In production IDistributedCache is Redis (AddStackExchangeRedisCache); dev/test fall back to an
    // in-memory distributed cache (AddDistributedMemoryCache) so no Redis server is needed. Entries
    // expire after the configured key lifetime (Idempotency:KeyLifetime, default 24h — §7.1); an
    // expired/absent key reads back as null → a new request.
    //
    // NOTE: TryBeginAsync is get-then-set, not one atomic operation. A production Redis store would
    // claim the key atomically with SET key value NX PX (StackExchange.Redis StringSet(..., When.NotExists))
    // to close the concurrent-claim race; IDistributedCache exposes no set-if-absent primitive. For this
    // sample the small window is acceptable and documented.
    public class DistributedCacheIdempotencyStore : IIdempotencyStore
    {
        private const string KeyPrefix = "idempotency:";

        private readonly IDistributedCache _cache;
        private readonly TimeSpan _keyLifetime;

        public DistributedCacheIdempotencyStore(IDistributedCache cache, TimeSpan keyLifetime)
        {
            _cache = cache;
            _keyLifetime = keyLifetime;
        }

        // The effective time-to-live applied to every entry written to the cache (§7.1).
        public TimeSpan KeyLifetime => _keyLifetime;

        public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken cancellationToken)
        {
            var json = await _cache.GetStringAsync(KeyPrefix + key, cancellationToken);
            return json == null ? null : JsonSerializer.Deserialize<IdempotencyRecord>(json);
        }

        public async Task<bool> TryBeginAsync(string key, string fingerprint, CancellationToken cancellationToken)
        {
            var cacheKey = KeyPrefix + key;
            var existing = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (existing != null)
            {
                return false;
            }

            await SetAsync(cacheKey, new IdempotencyRecord(fingerprint, IdempotencyStatus.InProgress, null), cancellationToken);
            return true;
        }

        public Task CompleteAsync(string key, string fingerprint, IdempotencyResponse response, CancellationToken cancellationToken)
        {
            return SetAsync(KeyPrefix + key, new IdempotencyRecord(fingerprint, IdempotencyStatus.Completed, response), cancellationToken);
        }

        public Task ReleaseAsync(string key, CancellationToken cancellationToken)
        {
            return _cache.RemoveAsync(KeyPrefix + key, cancellationToken);
        }

        private Task SetAsync(string cacheKey, IdempotencyRecord record, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(record);
            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _keyLifetime };
            return _cache.SetStringAsync(cacheKey, json, options, cancellationToken);
        }
    }
}
