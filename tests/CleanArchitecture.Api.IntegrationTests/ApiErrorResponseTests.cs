using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CleanArchitecture.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class ApiErrorResponseTests : IClassFixture<ErrorResponseTestFactory>
    {
        private readonly HttpClient _client;

        public ApiErrorResponseTests(ErrorResponseTestFactory factory)
        {
            _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        [Fact]
        public async Task Validation_400_Response_Has_Standard_Envelope_And_Errors()
        {
            var payload = new { name = "", description = "", price = -1m, stock = -1 };

            var resp = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertCommonEnvelope(problem, expectedStatus: 400, expectedCode: "VALIDATION_FAILED", expectedInstance: "/api/products");
            Assert.True(problem.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("Name", out _));
        }

        [Fact]
        public async Task NotFound_404_Response_Has_Standard_Envelope()
        {
            var resp = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertCommonEnvelope(problem, expectedStatus: 404, expectedCode: "RESOURCE_NOT_FOUND", expectedInstancePrefix: "/api/products/");
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
        }

        [Fact]
        public async Task Domain_400_Response_Has_DomainRuleViolated_Code()
        {
            var productId = await CreateProductAsync(price: 40m, stock: 2);

            var payload = new
            {
                customerName = "InsufficientStockBuyer",
                items = new[] { new { productId, quantity = 5 } }
            };

            var resp = await _client.PostAsJsonAsync("/api/orders/place", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertCommonEnvelope(problem, expectedStatus: 400, expectedCode: "DOMAIN_RULE_VIOLATED", expectedInstance: "/api/orders/place");
        }

        [Fact]
        public async Task Derived_Domain_Exception_Maps_To_400_DomainRuleViolated()
        {
            var resp = await _client.GetAsync("/api/_test/throw-derived-domain");

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertCommonEnvelope(problem, expectedStatus: 400, expectedCode: "DOMAIN_RULE_VIOLATED", expectedInstance: "/api/_test/throw-derived-domain");
        }

        [Fact]
        public async Task Concurrency_Conflict_Maps_To_409_With_Standard_Envelope()
        {
            var resp = await _client.GetAsync("/api/_test/throw-concurrency");

            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertCommonEnvelope(problem, expectedStatus: 409, expectedCode: "CONCURRENCY_CONFLICT", expectedInstance: "/api/_test/throw-concurrency");
        }

        [Fact]
        public async Task Unknown_500_Response_Does_Not_Leak_Internal_Message()
        {
            var resp = await _client.GetAsync("/api/_test/throw");

            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("internal-secret-do-not-leak", body);
            Assert.DoesNotContain("InvalidOperationException", body);

            var problem = JsonSerializer.Deserialize<JsonElement>(body);
            AssertCommonEnvelope(problem, expectedStatus: 500, expectedCode: "INTERNAL_ERROR", expectedInstance: "/api/_test/throw");
        }

        private static void AssertCommonEnvelope(
            JsonElement problem,
            int expectedStatus,
            string expectedCode,
            string? expectedInstance = null,
            string? expectedInstancePrefix = null)
        {
            Assert.Equal(expectedStatus, problem.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("type").GetString()));
            Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("timestamp").GetString()));

            var instance = problem.GetProperty("instance").GetString();
            if (expectedInstance != null)
            {
                Assert.Equal(expectedInstance, instance);
            }
            else if (expectedInstancePrefix != null)
            {
                Assert.NotNull(instance);
                Assert.StartsWith(expectedInstancePrefix, instance);
            }
        }

        private async Task<Guid> CreateProductAsync(decimal price, int stock)
        {
            var payload = new
            {
                name = "ERR-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                description = "for error envelope test",
                price,
                stock
            };
            var resp = await _client.PostAsJsonAsync("/api/products", payload);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return dto.GetProperty("id").GetGuid();
        }
    }
}
