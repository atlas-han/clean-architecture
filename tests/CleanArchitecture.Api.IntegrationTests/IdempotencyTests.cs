using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // Idempotency-Key handling on the order-creating POSTs (API design guide §7.1). The store is an
    // in-memory distributed cache (no Redis configured), shared process-wide across this class's tests,
    // so each test uses a fresh key/product to stay order-independent.
    public class IdempotencyTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        public IdempotencyTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        private async Task<Guid> CreateProductAsync(decimal price = 100m, int stock = 10)
        {
            var payload = new
            {
                name = "P-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                description = "for idempotency test",
                price,
                stock
            };
            var resp = await _client.PostAsJsonAsync("/api/v1/products", payload);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("data").GetProperty("id").GetGuid();
        }

        private Task<HttpResponseMessage> PostWithKeyAsync(string url, object payload, string key)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Add("Idempotency-Key", key);
            return _client.SendAsync(request);
        }

        private async Task<int> GetStockAsync(Guid productId)
        {
            var resp = await _client.GetAsync($"/api/v1/products/{productId}");
            var data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            return data.GetProperty("stock").GetInt32();
        }

        [Fact]
        public async Task SameKey_ReplaysOriginalResponse_AndExecutesOnce()
        {
            var productId = await CreateProductAsync(price: 40m, stock: 10);
            var key = Guid.NewGuid().ToString();
            var payload = new
            {
                customerName = "Idem-" + key.Substring(0, 4),
                items = new[] { new { productId, quantity = 3 } }
            };

            var first = await PostWithKeyAsync("/api/v1/orders/place", payload, key);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.False(first.Headers.Contains("Idempotency-Replayed"));
            var firstBody = await first.Content.ReadAsStringAsync();

            var second = await PostWithKeyAsync("/api/v1/orders/place", payload, key);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            var secondBody = await second.Content.ReadAsStringAsync();

            // Replay: byte-identical original response (same id, traceId, timestamp) + the replay marker.
            Assert.Equal(firstBody, secondBody);
            Assert.True(second.Headers.Contains("Idempotency-Replayed"));

            // Executed exactly once: stock fell by 3 (not 6) despite the duplicate POST.
            Assert.Equal(7, await GetStockAsync(productId));
        }

        [Fact]
        public async Task DifferentKeys_CreateSeparateOrders()
        {
            var productId = await CreateProductAsync(price: 10m, stock: 10);
            var payload = new
            {
                customerName = "Multi",
                items = new[] { new { productId, quantity = 1 } }
            };

            var r1 = await PostWithKeyAsync("/api/v1/orders/place", payload, Guid.NewGuid().ToString());
            var r2 = await PostWithKeyAsync("/api/v1/orders/place", payload, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
            Assert.Equal(HttpStatusCode.Created, r2.StatusCode);

            var id1 = (await r1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
            var id2 = (await r2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("id").GetGuid();
            Assert.NotEqual(id1, id2);

            // Both executed: stock fell by 2.
            Assert.Equal(8, await GetStockAsync(productId));
        }

        [Fact]
        public async Task SameKey_DifferentPayload_Returns409()
        {
            var productId = await CreateProductAsync(price: 10m, stock: 10);
            var key = Guid.NewGuid().ToString();

            var a = await PostWithKeyAsync("/api/v1/orders",
                new { customerName = "A", items = new[] { new { productId, quantity = 1 } } }, key);
            Assert.Equal(HttpStatusCode.Created, a.StatusCode);

            var b = await PostWithKeyAsync("/api/v1/orders",
                new { customerName = "B", items = new[] { new { productId, quantity = 2 } } }, key);
            Assert.Equal(HttpStatusCode.Conflict, b.StatusCode);
            var err = await b.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("CONFLICT", err.GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task KeyAlreadyHeld_Returns409()
        {
            // Seed a claim directly in the shared store, then a request with that key must conflict.
            var key = Guid.NewGuid().ToString();
            var store = _factory.Services.GetRequiredService<IIdempotencyStore>();
            await store.TryBeginAsync(key, "seeded-fingerprint", CancellationToken.None);

            var productId = await CreateProductAsync();
            var resp = await PostWithKeyAsync("/api/v1/orders",
                new { customerName = "Held", items = new[] { new { productId, quantity = 1 } } }, key);

            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        }

        [Fact]
        public async Task FailedFirstAttempt_ReleasesClaim_AllowingRetry()
        {
            // §7.4: a non-2xx first attempt must release the claim so the same key can be retried.
            var key = Guid.NewGuid().ToString();

            // First attempt targets an unknown product → 404 (handler throws, exception filter shapes it).
            var failed = await PostWithKeyAsync("/api/v1/orders",
                new { customerName = "Retry", items = new[] { new { productId = Guid.NewGuid(), quantity = 1 } } }, key);
            Assert.Equal(HttpStatusCode.NotFound, failed.StatusCode);

            // Retry with the SAME key but a valid payload must succeed — the claim was released, so this
            // is treated as a fresh request rather than a 409 conflict.
            var validProductId = await CreateProductAsync(stock: 5);
            var retried = await PostWithKeyAsync("/api/v1/orders",
                new { customerName = "Retry", items = new[] { new { productId = validProductId, quantity = 1 } } }, key);
            Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        }

        [Fact]
        public async Task NoKey_BehavesNormally()
        {
            // Regression: an order POST without an Idempotency-Key is unchanged (no dedup, no marker).
            var productId = await CreateProductAsync();
            var resp = await _client.PostAsJsonAsync("/api/v1/orders",
                new { customerName = "NoKey", items = new[] { new { productId, quantity = 1 } } });

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            Assert.False(resp.Headers.Contains("Idempotency-Replayed"));
        }
    }
}
