using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CleanArchitecture.Api.IntegrationTests.Infrastructure;
using CleanArchitecture.Api.Middleware;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // API design guide §7.4 — X-Request-Deadline propagation / fast-fail.
    public class DeadlinePropagationMiddlewareTests
        : IClassFixture<WebApplicationFactory<Program>>, IClassFixture<ErrorResponseTestFactory>
    {
        private readonly WebApplicationFactory<Program> _factory;
        // ErrorResponseTestFactory loads the test-only ThrowingTestController (/api/_test/*),
        // including the deadline-echo probe used to verify HttpContext.Items population.
        private readonly ErrorResponseTestFactory _probeFactory;

        public DeadlinePropagationMiddlewareTests(WebApplicationFactory<Program> factory, ErrorResponseTestFactory probeFactory)
        {
            _factory = factory;
            _probeFactory = probeFactory;
        }

        private HttpClient CreateClient()
            => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        private static long EpochMs(TimeSpan fromNow)
            => DateTimeOffset.UtcNow.Add(fromNow).ToUnixTimeMilliseconds();

        private static async Task<HttpResponseMessage> GetHealthAsync(HttpClient client, string? deadline)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            if (deadline != null)
            {
                req.Headers.Add(DeadlinePropagationMiddleware.DeadlineHeader, deadline);
            }
            return await client.SendAsync(req);
        }

        [Fact]
        public async Task NoDeadlineHeader_RequestProceeds()
        {
            var resp = await GetHealthAsync(CreateClient(), deadline: null);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task LiveDeadline_RequestProceeds()
        {
            // 10s of budget left — well above the 50ms margin.
            var resp = await GetHealthAsync(CreateClient(), EpochMs(TimeSpan.FromSeconds(10)).ToString());
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task MalformedDeadline_IsIgnored_RequestProceeds()
        {
            var resp = await GetHealthAsync(CreateClient(), "not-a-number");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task ExpiredDeadline_FastFails_With504AndStandardEnvelope()
        {
            var resp = await GetHealthAsync(CreateClient(), EpochMs(TimeSpan.FromSeconds(-5)).ToString());

            Assert.Equal(HttpStatusCode.GatewayTimeout, resp.StatusCode);

            // §4.3 ErrorResponse envelope: top-level traceId + timestamp, error{code,message}.
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("timestamp").GetString()));
            var error = body.GetProperty("error");
            Assert.Equal("DEADLINE_EXCEEDED", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
            // Non-validation error → details omitted entirely (§4.3), not null/[].
            Assert.False(error.TryGetProperty("details", out _));
        }

        [Fact]
        public async Task NearExpiredDeadline_BelowMargin_FastFails()
        {
            // 20ms left is under the 50ms minimum budget, so it must fail fast (never reaches the endpoint).
            var resp = await GetHealthAsync(CreateClient(), EpochMs(TimeSpan.FromMilliseconds(20)).ToString());

            Assert.Equal(HttpStatusCode.GatewayTimeout, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("DEADLINE_EXCEEDED", body.GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task LiveDeadline_IsStoredInHttpContextItems()
        {
            var client = _probeFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var epoch = EpochMs(TimeSpan.FromSeconds(10));

            var req = new HttpRequestMessage(HttpMethod.Get, "/api/_test/deadline-echo");
            req.Headers.Add(DeadlinePropagationMiddleware.DeadlineHeader, epoch.ToString());
            var resp = await client.SendAsync(req);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            // Middleware stores the exact deadline; the round-trip reproduces the sent epoch.
            Assert.Equal(epoch, body.GetProperty("stored").GetInt64());
        }

        [Fact]
        public async Task NoDeadlineHeader_DoesNotStoreDeadline()
        {
            var client = _probeFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var resp = await client.GetAsync("/api/_test/deadline-echo");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, body.GetProperty("stored").ValueKind);
        }

        [Fact]
        public async Task LiveDeadline_CancelsInFlightWork_With504()
        {
            var client = _probeFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // 500ms budget clears the 50ms entry gate, so /slow starts; its 30s delay observes the
            // deadline token (§7.4 step 2) and is cancelled at ~500ms → OperationCanceledException
            // → ApiExceptionFilter maps it to 504 DEADLINE_EXCEEDED (client still connected).
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/_test/slow");
            req.Headers.Add(DeadlinePropagationMiddleware.DeadlineHeader, EpochMs(TimeSpan.FromMilliseconds(500)).ToString());
            var resp = await client.SendAsync(req);

            Assert.Equal(HttpStatusCode.GatewayTimeout, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("DEADLINE_EXCEEDED", body.GetProperty("error").GetProperty("code").GetString());
        }
    }
}
