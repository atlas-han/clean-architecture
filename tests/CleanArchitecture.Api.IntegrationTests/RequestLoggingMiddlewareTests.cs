using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CleanArchitecture.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class RequestLoggingMiddlewareTests : IClassFixture<LoggingTestFactory>
    {
        private const string MiddlewareCategory = "CleanArchitecture.Api.Middleware.RequestLoggingMiddleware";

        private readonly LoggingTestFactory _factory;

        public RequestLoggingMiddlewareTests(LoggingTestFactory factory)
        {
            _factory = factory;
        }

        private HttpClient CreateClient()
        {
            _factory.Provider.Clear();
            return _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        }

        private CapturedLogEntry GetRequestLog()
        {
            var entry = _factory.Provider.Entries
                .FirstOrDefault(e => e.Category == MiddlewareCategory);
            Assert.NotNull(entry);
            return entry!;
        }

        [Fact]
        public async Task SuccessfulRequest_EmitsInfoAccessLog_WithUnifiedSpecFields()
        {
            var client = CreateClient();

            var resp = await client.GetAsync("/health?probe=1");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var log = GetRequestLog();
            Assert.Equal(LogLevel.Information, log.Level);

            // §14.3 unified spec — field names are a fixed contract, casing included.
            Assert.Equal("GET", log.Values["method"]);
            Assert.Equal("/health?probe=1", log.Values["pathname"]);
            Assert.Equal(200, log.Values["status_code"]);
            Assert.Equal("Development", log.Values["api_environment"]);
            Assert.Equal(Environment.MachineName, log.Values["server_hostname"]);
            Assert.False(string.IsNullOrEmpty(log.Values["host"] as string));
            Assert.False(string.IsNullOrEmpty(log.Values["trace_id"] as string));
            Assert.False(string.IsNullOrEmpty(log.Values["request_id"] as string));
            Assert.True(log.Values.ContainsKey("span_id"));
            Assert.True(log.Values.ContainsKey("remote_addr"));
            Assert.True(log.Values.ContainsKey("endpoint_handler"));
            Assert.True(log.Values.ContainsKey("req_body_bytes"));
            Assert.True(log.Values.ContainsKey("res_body_bytes"));

            var latency = Assert.IsType<double>(log.Values["latency_ms"]);
            Assert.True(latency >= 0);

            Assert.StartsWith("HTTP GET /health?probe=1 -> 200 (", log.Message);
        }

        [Fact]
        public async Task RequestUuid_UsesXRequestIdHeader_WhenPresent()
        {
            var client = CreateClient();
            var supplied = "my-req-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            req.Headers.Add("X-Request-Id", supplied);

            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal(supplied, resp.Headers.GetValues("X-Request-Id").Single());

            var log = GetRequestLog();
            Assert.Equal(supplied, log.Values["request_id"]);
        }

        [Fact]
        public async Task RequestUuid_UsesCorrelationIdHeader_WhenPresent()
        {
            var client = CreateClient();
            var supplied = "corr-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            req.Headers.Add("X-Correlation-Id", supplied);

            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var log = GetRequestLog();
            Assert.Equal(supplied, log.Values["request_id"]);
        }

        [Fact]
        public async Task DebugLevel_IncludesResponseBody()
        {
            var client = CreateClient();

            var resp = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var log = GetRequestLog();
            Assert.True(log.Values.ContainsKey("response_body"));
            var body = log.Values["response_body"] as string;
            Assert.False(string.IsNullOrEmpty(body));
            Assert.Contains("\"status\"", body!);

            var resBodyBytes = Assert.IsType<long>(log.Values["res_body_bytes"]);
            Assert.True(resBodyBytes > 0);
        }

        [Fact]
        public async Task ClientError_LogsAtWarning()
        {
            var client = CreateClient();

            var resp = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

            var log = GetRequestLog();
            Assert.Equal(LogLevel.Warning, log.Level);
            Assert.Equal(404, log.Values["status_code"]);
        }

        [Fact]
        public async Task RequestBody_IsCapturedOnPost_AtDebugLevel()
        {
            var client = CreateClient();

            var payload = new { name = "ProbeProduct", description = "x", price = 1m, stock = 1 };
            var resp = await client.PostAsJsonAsync("/api/products", payload);
            Assert.True(resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.BadRequest);

            var log = GetRequestLog();
            var body = log.Values["request_body"] as string;
            Assert.False(string.IsNullOrEmpty(body));
            Assert.Contains("ProbeProduct", body!);

            var reqBodyBytes = Assert.IsType<long>(log.Values["req_body_bytes"]);
            Assert.True(reqBodyBytes > 0);
        }

        [Fact]
        public async Task RequestBody_MasksPiiFields_AtDebugLevel()
        {
            var client = CreateClient();

            // customerName is PII; order creation may be rejected (e.g. empty items),
            // but the request body is logged regardless — that is what we assert on.
            var payload = new { customerName = "Secret", items = new object[0] };
            await client.PostAsJsonAsync("/api/orders", payload);

            var log = GetRequestLog();
            var body = log.Values["request_body"] as string;
            Assert.False(string.IsNullOrEmpty(body));

            // Raw PII must never appear; the value is partially masked (first char kept).
            Assert.DoesNotContain("Secret", body!);
            using var doc = JsonDocument.Parse(body!);
            Assert.Equal("S*****", doc.RootElement.GetProperty("customerName").GetString());
        }
    }

    public class RequestLoggingMiddlewareInfoLevelTests : IClassFixture<InfoLoggingTestFactory>
    {
        private const string MiddlewareCategory = "CleanArchitecture.Api.Middleware.RequestLoggingMiddleware";

        private readonly InfoLoggingTestFactory _factory;

        public RequestLoggingMiddlewareInfoLevelTests(InfoLoggingTestFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task InfoLevel_OmitsRequestAndResponseBodies()
        {
            _factory.Provider.Clear();
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var payload = new { name = "ProbeProduct", description = "x", price = 1m, stock = 1 };
            var resp = await client.PostAsJsonAsync("/api/products", payload);
            Assert.True(resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.BadRequest);

            var log = _factory.Provider.Entries.FirstOrDefault(e => e.Category == MiddlewareCategory);
            Assert.NotNull(log);

            // §14.6: bodies are off by default outside debug paths; byte counts remain.
            Assert.False(log!.Values.ContainsKey("request_body"));
            Assert.False(log.Values.ContainsKey("response_body"));
            Assert.True(log.Values.ContainsKey("req_body_bytes"));
            Assert.True(log.Values.ContainsKey("res_body_bytes"));
        }
    }
}
