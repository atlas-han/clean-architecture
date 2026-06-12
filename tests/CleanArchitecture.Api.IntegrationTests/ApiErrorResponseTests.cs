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
        public async Task Validation_400_Response_Has_ErrorEnvelope_And_FieldDetails()
        {
            var payload = new { name = "", description = "", price = -1m, stock = -1 };

            var resp = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertErrorEnvelope(body, expectedCode: "VALIDATION_ERROR");

            var details = body.GetProperty("error").GetProperty("details");
            Assert.Equal(JsonValueKind.Array, details.ValueKind);
            Assert.True(HasFieldError(details, "Name"));
        }

        [Fact]
        public async Task NotFound_404_Response_Has_ErrorEnvelope_Without_Details()
        {
            var resp = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertErrorEnvelope(body, expectedCode: "NOT_FOUND");
            // details is omitted entirely (not null, not []) for non-validation errors (§4.3).
            Assert.False(body.GetProperty("error").TryGetProperty("details", out _));
        }

        [Fact]
        public async Task Domain_Violation_Maps_To_422_BusinessRuleViolation()
        {
            var productId = await CreateProductAsync(price: 40m, stock: 2);

            var payload = new
            {
                customerName = "InsufficientStockBuyer",
                items = new[] { new { productId, quantity = 5 } }
            };

            var resp = await _client.PostAsJsonAsync("/api/orders/place", payload);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertErrorEnvelope(body, expectedCode: "BUSINESS_RULE_VIOLATION");
        }

        [Fact]
        public async Task Derived_Domain_Exception_Maps_To_422_BusinessRuleViolation()
        {
            var resp = await _client.GetAsync("/api/_test/throw-derived-domain");

            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertErrorEnvelope(body, expectedCode: "BUSINESS_RULE_VIOLATION");
        }

        [Fact]
        public async Task Concurrency_Conflict_Maps_To_409_With_ErrorEnvelope()
        {
            var resp = await _client.GetAsync("/api/_test/throw-concurrency");

            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();

            AssertErrorEnvelope(body, expectedCode: "CONFLICT");
        }

        [Fact]
        public async Task Unknown_500_Response_Does_Not_Leak_Internal_Message()
        {
            var resp = await _client.GetAsync("/api/_test/throw");

            Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
            var raw = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("internal-secret-do-not-leak", raw);
            Assert.DoesNotContain("InvalidOperationException", raw);

            var body = JsonSerializer.Deserialize<JsonElement>(raw);
            AssertErrorEnvelope(body, expectedCode: "INTERNAL_ERROR");
        }

        // Every error shares the §4.3 envelope: top-level traceId + timestamp and an
        // error object with code + message. Clients branch on the HTTP status code.
        private static void AssertErrorEnvelope(JsonElement body, string expectedCode)
        {
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("timestamp").GetString()));

            var error = body.GetProperty("error");
            Assert.Equal(expectedCode, error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        }

        private static bool HasFieldError(JsonElement details, string field)
        {
            foreach (var entry in details.EnumerateArray())
            {
                if (entry.GetProperty("field").GetString() == field)
                {
                    return true;
                }
            }
            return false;
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
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("data").GetProperty("id").GetGuid();
        }
    }
}
