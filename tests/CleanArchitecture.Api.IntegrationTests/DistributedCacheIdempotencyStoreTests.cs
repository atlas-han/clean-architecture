using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Infrastructure.Idempotency;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // Store-level mechanics of the §7.1 idempotency store, exercised against an in-memory
    // IDistributedCache (the same abstraction Redis sits behind in production).
    public class DistributedCacheIdempotencyStoreTests
    {
        private static DistributedCacheIdempotencyStore CreateStore()
        {
            var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
            return new DistributedCacheIdempotencyStore(cache);
        }

        [Fact]
        public async Task TryBegin_ClaimsKeyOnce_SecondClaimFails()
        {
            var store = CreateStore();

            Assert.True(await store.TryBeginAsync("k", "fp", CancellationToken.None));
            Assert.False(await store.TryBeginAsync("k", "fp", CancellationToken.None));

            var record = await store.GetAsync("k", CancellationToken.None);
            Assert.NotNull(record);
            Assert.Equal(IdempotencyStatus.InProgress, record!.Status);
            Assert.Equal("fp", record.Fingerprint);
            Assert.Null(record.Response);
        }

        [Fact]
        public async Task Complete_StoresResponse_ForReplay()
        {
            var store = CreateStore();
            await store.TryBeginAsync("k", "fp", CancellationToken.None);

            var response = new IdempotencyResponse(201, "application/json", "{\"ok\":true}", "/api/v1/orders/1");
            await store.CompleteAsync("k", "fp", response, CancellationToken.None);

            var record = await store.GetAsync("k", CancellationToken.None);
            Assert.Equal(IdempotencyStatus.Completed, record!.Status);
            Assert.Equal(response, record.Response); // record value equality across the JSON round-trip
        }

        [Fact]
        public async Task Release_RemovesEntry_AllowingReclaim()
        {
            var store = CreateStore();
            await store.TryBeginAsync("k", "fp", CancellationToken.None);

            await store.ReleaseAsync("k", CancellationToken.None);

            Assert.Null(await store.GetAsync("k", CancellationToken.None));
            Assert.True(await store.TryBeginAsync("k", "fp2", CancellationToken.None));
        }

        [Fact]
        public async Task Get_UnknownKey_ReturnsNull()
        {
            var store = CreateStore();
            Assert.Null(await store.GetAsync("missing", CancellationToken.None));
        }
    }
}
