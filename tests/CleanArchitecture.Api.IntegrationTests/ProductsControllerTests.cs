using System;
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

        [Fact]
        public async Task Create_Then_Get_RoundTrip()
        {
            var payload = new
            {
                name = "IT-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                description = "integration test item",
                price = 12345m,
                stock = 7
            };

            var createResp = await _client.PostAsJsonAsync("/api/products", payload);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

            var id = await createResp.Content.ReadFromJsonAsync<Guid>();
            Assert.NotEqual(Guid.Empty, id);

            var getResp = await _client.GetAsync($"/api/products/{id}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

            var dto = await getResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(payload.name, dto.GetProperty("name").GetString());
            Assert.Equal(payload.price, dto.GetProperty("price").GetDecimal());
            Assert.Equal(payload.stock, dto.GetProperty("stock").GetInt32());
        }

        [Fact]
        public async Task Create_InvalidPayload_Returns_400_With_ValidationProblemDetails()
        {
            var payload = new { name = "", description = "", price = -1m, stock = -1 };

            var resp = await _client.PostAsJsonAsync("/api/products", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.TryGetProperty("errors", out var errors));
            Assert.True(errors.TryGetProperty("Name", out _));
            Assert.True(errors.TryGetProperty("Price", out _));
            Assert.True(errors.TryGetProperty("Stock", out _));
        }

        [Fact]
        public async Task Get_UnknownId_Returns_404()
        {
            var resp = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Update_UnknownId_Returns_404()
        {
            var id = Guid.NewGuid();
            var payload = new { id, name = "x", description = "y", price = 1m, stock = 1 };

            var resp = await _client.PutAsJsonAsync($"/api/products/{id}", payload);

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
            var createResp = await _client.PostAsJsonAsync("/api/products", payload);
            var id = await createResp.Content.ReadFromJsonAsync<Guid>();

            var deleteResp = await _client.DeleteAsync($"/api/products/{id}");
            Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

            var getResp = await _client.GetAsync($"/api/products/{id}");
            Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
        }
    }
}
