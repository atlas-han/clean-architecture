using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CleanArchitecture.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class RequestLoggingMiddlewareTests : IClassFixture<LoggingTestFactory>
    {
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
                .FirstOrDefault(e => e.Category == "CleanArchitecture.Api.Middleware.RequestLoggingMiddleware");
            Assert.NotNull(entry);
            return entry!;
        }

        [Fact]
        public async Task SuccessfulRequest_EmitsInfoLog_WithRequiredFields()
        {
            var client = CreateClient();

            var resp = await client.GetAsync("/health?probe=1");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var log = GetRequestLog();
            Assert.Equal(LogLevel.Information, log.Level);

            Assert.Equal("/health", log.Values["Path"]);
            Assert.Equal("?probe=1", log.Values["QueryString"]);
            Assert.Equal("GET", log.Values["Method"]);
            Assert.Equal(200, log.Values["StatusCode"]);
            Assert.True(log.Values.ContainsKey("Timestamp"));
            Assert.True(log.Values.ContainsKey("TraceId"));
            Assert.True(log.Values.ContainsKey("RequestId"));
            Assert.True(log.Values.ContainsKey("ProcessingTimeMs"));

            var elapsed = Assert.IsType<double>(log.Values["ProcessingTimeMs"]);
            Assert.True(elapsed >= 0);
        }

        [Fact]
        public async Task RequestId_UsesXRequestIdHeader_WhenPresent()
        {
            var client = CreateClient();
            var supplied = "my-req-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            req.Headers.Add("X-Request-Id", supplied);

            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Equal(supplied, resp.Headers.GetValues("X-Request-Id").Single());

            var log = GetRequestLog();
            Assert.Equal(supplied, log.Values["RequestId"]);
        }

        [Fact]
        public async Task RequestId_UsesCorrelationIdHeader_WhenPresent()
        {
            var client = CreateClient();
            var supplied = "corr-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var req = new HttpRequestMessage(HttpMethod.Get, "/health");
            req.Headers.Add("X-Correlation-Id", supplied);

            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var log = GetRequestLog();
            Assert.Equal(supplied, log.Values["RequestId"]);
        }

        [Fact]
        public async Task DebugLevel_IncludesResponseBody()
        {
            var client = CreateClient();

            var resp = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var log = GetRequestLog();
            Assert.True(log.Values.ContainsKey("ResponseBody"));
            var body = log.Values["ResponseBody"] as string;
            Assert.False(string.IsNullOrEmpty(body));
            Assert.Contains("\"status\"", body!);
        }

        [Fact]
        public async Task ClientError_LogsAtWarning()
        {
            var client = CreateClient();

            var resp = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

            var log = GetRequestLog();
            Assert.Equal(LogLevel.Warning, log.Level);
            Assert.Equal(404, log.Values["StatusCode"]);
        }

        [Fact]
        public async Task RequestBody_IsCapturedOnPost()
        {
            var client = CreateClient();

            var payload = new { name = "ProbeProduct", description = "x", price = 1m, stock = 1 };
            var resp = await client.PostAsJsonAsync("/api/products", payload);
            Assert.True(resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.BadRequest);

            var log = GetRequestLog();
            var body = log.Values["RequestBody"] as string;
            Assert.False(string.IsNullOrEmpty(body));
            Assert.Contains("ProbeProduct", body!);
        }
    }
}
