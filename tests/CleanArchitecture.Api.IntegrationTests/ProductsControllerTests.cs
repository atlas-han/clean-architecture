using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class ProductsControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public ProductsControllerTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        // Single-resource payloads live under the SuccessResponse `data` field (§4.2).
        private static async Task<Guid> ReadCreatedIdAsync(HttpResponseMessage resp)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("data").GetProperty("id").GetGuid();
        }

        [Fact]
        public async Task Create_ReturnsLocationAndResourceBody_RoundTrip()
        {
            var payload = new
            {
                name = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                description = "integration test item",
                price = 12345m,
                stock = 7
            };

            var createResp = await _client.PostAsJsonAsync("/api/v1/products", payload);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            Assert.NotNull(createResp.Headers.Location);

            var created = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            var id = created.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, id);
            Assert.Equal(payload.name, created.GetProperty("name").GetString());
            Assert.Equal(payload.price, created.GetProperty("price").GetDecimal());
            Assert.Equal(payload.stock, created.GetProperty("stock").GetInt32());

            var getResp = await _client.GetAsync($"/api/v1/products/{id}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

            var data = (await getResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            Assert.Equal(payload.name, data.GetProperty("name").GetString());
            Assert.Equal(payload.price, data.GetProperty("price").GetDecimal());
            Assert.Equal(payload.stock, data.GetProperty("stock").GetInt32());
        }

        [Fact]
        public async Task GetAll_ReturnsSuccessEnvelope_With_DataArray_And_Meta()
        {
            var payload = new
            {
                name = "PG-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                description = "for paging test",
                price = 10m,
                stock = 1
            };
            var createResp = await _client.PostAsJsonAsync("/api/v1/products", payload);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

            var listResp = await _client.GetAsync("/api/v1/products?page=1&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

            var envelope = await listResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Object, envelope.ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(envelope.GetProperty("traceId").GetString()));
            // List payload is a bare array under `data` (§4.2).
            Assert.Equal(JsonValueKind.Array, envelope.GetProperty("data").ValueKind);

            // Pagination lives in `meta` (§4.2/§4.5 PaginationMeta).
            var meta = envelope.GetProperty("meta");
            Assert.True(meta.GetProperty("totalCount").GetInt32() >= 1);
            Assert.Equal(1, meta.GetProperty("page").GetInt32());
            Assert.Equal(100, meta.GetProperty("pageSize").GetInt32());
            Assert.True(meta.TryGetProperty("totalPages", out _));
        }

        [Fact]
        public async Task Create_InvalidPayload_Returns_400_With_FieldErrors()
        {
            var payload = new { name = "", description = "", price = -1m, stock = -1 };

            var resp = await _client.PostAsJsonAsync("/api/v1/products", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());

            var fields = new HashSet<string>();
            foreach (var entry in body.GetProperty("error").GetProperty("details").EnumerateArray())
            {
                fields.Add(entry.GetProperty("field").GetString()!);
            }
            Assert.Contains("Name", fields);
            Assert.Contains("Price", fields);
            Assert.Contains("Stock", fields);
        }

        [Fact]
        public async Task Get_UnknownId_Returns_404()
        {
            var resp = await _client.GetAsync($"/api/v1/products/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Update_UnknownId_Returns_404()
        {
            var id = Guid.NewGuid();
            var payload = new { id, name = "x", description = "y", price = 1m, stock = 1 };

            var resp = await _client.PutAsJsonAsync($"/api/v1/products/{id}", payload);

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Delete_Then_Get_Returns_404()
        {
            var payload = new
            {
                name = "DEL-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                description = "to be deleted",
                price = 1m,
                stock = 1
            };
            var createResp = await _client.PostAsJsonAsync("/api/v1/products", payload);
            var id = await ReadCreatedIdAsync(createResp);

            var deleteResp = await _client.DeleteAsync($"/api/v1/products/{id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

            var getResp = await _client.GetAsync($"/api/v1/products/{id}");
            Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
        }
    }
}
