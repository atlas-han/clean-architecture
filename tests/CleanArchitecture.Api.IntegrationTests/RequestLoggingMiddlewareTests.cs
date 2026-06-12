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
            Assert.Equal("GET", log.Values["http.request.method"]);
            // pathname is split into path + query_string; query_string drops the leading '?'.
            Assert.Equal("/health", log.Values["path"]);
            Assert.Equal("probe=1", log.Values["query_string"]);
            Assert.Equal(200, log.Values["http.response.status_code"]);
            Assert.Equal("Development", log.Values["api_environment"]);
            Assert.Equal(Environment.MachineName, log.Values["server_hostname"]);
            Assert.False(string.IsNullOrEmpty(log.Values["host"] as string));
            Assert.False(string.IsNullOrEmpty(log.Values["trace_id"] as string));
            Assert.False(string.IsNullOrEmpty(log.Values["request_id"] as string));
            Assert.True(log.Values.ContainsKey("span_id"));
            Assert.True(log.Values.ContainsKey("client.address"));
            Assert.True(log.Values.ContainsKey("endpoint_handler"));
            Assert.True(log.Values.ContainsKey("req_body_bytes"));
            Assert.True(log.Values.ContainsKey("res_body_bytes"));

            var duration = Assert.IsType<double>(log.Values["duration"]);
            Assert.True(duration >= 0);

            Assert.StartsWith("HTTP GET /health?probe=1 -> 200 (", log.Message);
        }

        [Fact]
        public async Task RequestWithoutQuery_OmitsQueryStringField()
        {
            var client = CreateClient();

            var resp = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // §14.3: path is always present; query_string is dropped when there is no query.
            var log = GetRequestLog();
            Assert.Equal("/health", log.Values["path"]);
            Assert.False(log.Values.ContainsKey("query_string"));
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

            var resp = await client.GetAsync($"/api/v1/orders/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

            var log = GetRequestLog();
            Assert.Equal(LogLevel.Warning, log.Level);
            Assert.Equal(404, log.Values["http.response.status_code"]);
        }

        [Fact]
        public async Task RequestBody_IsCapturedOnPost_AtDebugLevel()
        {
            var client = CreateClient();

            var payload = new { name = "ProbeProduct", description = "x", price = 1m, stock = 1 };
            var resp = await client.PostAsJsonAsync("/api/v1/products", payload);
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
            await client.PostAsJsonAsync("/api/v1/orders", payload);

            var log = GetRequestLog();
            var body = log.Values["request_body"] as string;
            Assert.False(string.IsNullOrEmpty(body));

            // Raw PII must never appear; the value is partially masked (first char kept).
            Assert.DoesNotContain("Secret", body!);
            using var doc = JsonDocument.Parse(body!);
            Assert.Equal("S*****", doc.RootElement.GetProperty("customerName").GetString());
        }

        [Fact]
        public async Task RequestHeaders_AreLogged_WithReqHeaderPrefix()
        {
            var client = CreateClient();

            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            req.Headers.TryAddWithoutValidation("X-Debug-Tag", "trace-me");

            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // §14.3 prefix convention: one field per header, name lowercased.
            var log = GetRequestLog();
            Assert.Equal("trace-me", log.Values["req_header_x-debug-tag"]);

            // host/content-length request headers are excluded from the access log.
            Assert.False(log.Values.ContainsKey("req_header_host"));
            Assert.False(log.Values.ContainsKey("req_header_content-length"));
        }

        [Fact]
        public async Task SensitiveRequestHeaders_AreMasked()
        {
            // Cookie is managed by the cookie container by default; turn it off so the
            // header we set reaches the server verbatim and exercises the mask path.
            _factory.Provider.Clear();
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false
            });

            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer super-secret-token");
            req.Headers.TryAddWithoutValidation("X-Api-Key", "ak_live_should_not_leak");
            req.Headers.TryAddWithoutValidation("Cookie", "session=hidden-session-value");

            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // §14.6: sensitive headers are redacted; the raw secret never reaches the log.
            var log = GetRequestLog();
            Assert.Equal("***", log.Values["req_header_authorization"]);
            Assert.Equal("***", log.Values["req_header_x-api-key"]);
            Assert.Equal("***", log.Values["req_header_cookie"]);

            foreach (var value in log.Values.Values)
            {
                var text = value as string;
                if (text == null) continue;
                Assert.DoesNotContain("super-secret-token", text);
                Assert.DoesNotContain("ak_live_should_not_leak", text);
                Assert.DoesNotContain("hidden-session-value", text);
            }
        }

        [Fact]
        public async Task ResponseHeaders_AreLogged_WithResHeaderPrefix()
        {
            var client = CreateClient();

            var resp = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // Response headers surface as res_header_* fields (name lowercased).
            var log = GetRequestLog();
            Assert.True(log.Values.ContainsKey("res_header_content-type"));

            // Low-signal/identity response headers are excluded from the access log.
            Assert.False(log.Values.ContainsKey("res_header_x-request-id"));
            Assert.False(log.Values.ContainsKey("res_header_date"));
            Assert.False(log.Values.ContainsKey("res_header_server"));
        }

        [Fact]
        public async Task LocationResponseHeader_IsExcluded_OnCreated()
        {
            var client = CreateClient();

            // A successful create returns 201 + a Location header; it must not surface in the log.
            var payload = new { name = "LocProbe-" + Guid.NewGuid().ToString("N").Substring(0, 8), description = "x", price = 1m, stock = 1 };
            var resp = await client.PostAsJsonAsync("/api/v1/products", payload);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            Assert.NotNull(resp.Headers.Location);

            var log = GetRequestLog();
            Assert.False(log.Values.ContainsKey("res_header_location"));
        }

        [Fact]
        public async Task SensitiveResponseHeaders_AreMasked()
        {
            var client = CreateClient();

            // The probe endpoint sets a Set-Cookie response header (a credential carrier).
            var resp = await client.GetAsync("/api/_test/set-cookie");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // §14.6: response-side masking applies on the res_header_* path too.
            var log = GetRequestLog();
            Assert.Equal("***", log.Values["res_header_set-cookie"]);

            foreach (var value in log.Values.Values)
            {
                if (value is string text)
                {
                    Assert.DoesNotContain("secret-cookie-value", text);
                }
            }
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
            var resp = await client.PostAsJsonAsync("/api/v1/products", payload);
            Assert.True(resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.BadRequest);

            var log = _factory.Provider.Entries.FirstOrDefault(e => e.Category == MiddlewareCategory);
            Assert.NotNull(log);

            // §14.6: bodies are off by default outside debug paths; byte counts remain.
            Assert.False(log!.Values.ContainsKey("request_body"));
            Assert.False(log.Values.ContainsKey("response_body"));
            Assert.True(log.Values.ContainsKey("req_body_bytes"));
            Assert.True(log.Values.ContainsKey("res_body_bytes"));
        }

        [Fact]
        public async Task InfoLevel_StillLogsHeaders()
        {
            _factory.Provider.Clear();
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            req.Headers.TryAddWithoutValidation("X-Debug-Tag", "trace-me");

            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var log = _factory.Provider.Entries.FirstOrDefault(e => e.Category == MiddlewareCategory);
            Assert.NotNull(log);

            // Unlike bodies, headers (§14.3) are logged at every level, not just debug paths.
            Assert.Equal("trace-me", log!.Values["req_header_x-debug-tag"]);
            Assert.True(log.Values.ContainsKey("res_header_content-type"));
        }
    }
}
