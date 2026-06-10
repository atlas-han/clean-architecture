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
    public class OrdersControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public OrdersControllerTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        private static async Task<Guid> ReadCreatedIdAsync(HttpResponseMessage resp)
        {
            var dto = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return dto.GetProperty("id").GetGuid();
        }

        private async Task<Guid> CreateProductAsync(decimal price = 100m, int stock = 10)
        {
            var payload = new
            {
                name = "P-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                description = "for order test",
                price,
                stock
            };
            var resp = await _client.PostAsJsonAsync("/api/products", payload);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            return await ReadCreatedIdAsync(resp);
        }

        [Fact]
        public async Task Create_Then_Get_RoundTrip()
        {
            var productId = await CreateProductAsync(price: 50m);

            var orderPayload = new
            {
                customerName = "Alice-" + Guid.NewGuid().ToString("N").Substring(0, 4),
                items = new[]
                {
                    new { productId, quantity = 3 }
                }
            };

            var createResp = await _client.PostAsJsonAsync("/api/orders", orderPayload);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
            Assert.NotNull(createResp.Headers.Location);

            var createdDto = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            var id = createdDto.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, id);
            Assert.Equal(orderPayload.customerName, createdDto.GetProperty("customerName").GetString());
            Assert.Equal(150m, createdDto.GetProperty("totalAmount").GetDecimal());

            var getResp = await _client.GetAsync($"/api/orders/{id}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

            var dto = await getResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(orderPayload.customerName, dto.GetProperty("customerName").GetString());
            Assert.Equal(150m, dto.GetProperty("totalAmount").GetDecimal());
            Assert.Equal(1, dto.GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task Create_InvalidPayload_Returns_400()
        {
            var payload = new { customerName = "", items = new object[0] };

            var resp = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var problem = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(problem.TryGetProperty("errors", out _));
        }

        [Fact]
        public async Task Create_InsufficientStock_Returns_400()
        {
            var productId = await CreateProductAsync(price: 40m, stock: 2);

            var payload = new
            {
                customerName = "Frank",
                items = new[] { new { productId, quantity = 5 } }
            };

            var resp = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Create_WithUnknownProductId_Returns_404()
        {
            var payload = new
            {
                customerName = "Bob",
                items = new[]
                {
                    new { productId = Guid.NewGuid(), quantity = 1 }
                }
            };

            var resp = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Get_UnknownId_Returns_404()
        {
            var resp = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Cancel_ExistingOrder_Returns_204_And_StatusBecomesCancelled()
        {
            var productId = await CreateProductAsync();
            var orderPayload = new
            {
                customerName = "Carol",
                items = new[] { new { productId, quantity = 1 } }
            };
            var createResp = await _client.PostAsJsonAsync("/api/orders", orderPayload);
            var id = await ReadCreatedIdAsync(createResp);

            var cancelResp = await _client.PostAsync($"/api/orders/{id}/cancel", null);
            Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode);

            var getResp = await _client.GetAsync($"/api/orders/{id}");
            var dto = await getResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, dto.GetProperty("status").GetInt32());
        }

        [Fact]
        public async Task Cancel_UnknownId_Returns_404()
        {
            var resp = await _client.PostAsync($"/api/orders/{Guid.NewGuid()}/cancel", null);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Confirm_ExistingOrder_Returns_204_And_StatusBecomesConfirmed()
        {
            var productId = await CreateProductAsync();
            var orderPayload = new
            {
                customerName = "Frank",
                items = new[] { new { productId, quantity = 1 } }
            };
            var createResp = await _client.PostAsJsonAsync("/api/orders", orderPayload);
            var id = await ReadCreatedIdAsync(createResp);

            var confirmResp = await _client.PostAsync($"/api/orders/{id}/confirm", null);
            Assert.Equal(HttpStatusCode.NoContent, confirmResp.StatusCode);

            var getResp = await _client.GetAsync($"/api/orders/{id}");
            var dto = await getResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, dto.GetProperty("status").GetInt32());
        }

        [Fact]
        public async Task Confirm_UnknownId_Returns_404()
        {
            var resp = await _client.PostAsync($"/api/orders/{Guid.NewGuid()}/confirm", null);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Confirm_CancelledOrder_Returns_400()
        {
            var productId = await CreateProductAsync();
            var orderPayload = new
            {
                customerName = "Grace",
                items = new[] { new { productId, quantity = 1 } }
            };
            var createResp = await _client.PostAsJsonAsync("/api/orders", orderPayload);
            var id = await ReadCreatedIdAsync(createResp);

            var cancelResp = await _client.PostAsync($"/api/orders/{id}/cancel", null);
            Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode);

            var confirmResp = await _client.PostAsync($"/api/orders/{id}/confirm", null);
            Assert.Equal(HttpStatusCode.BadRequest, confirmResp.StatusCode);
        }

        [Fact]
        public async Task Place_DecrementsStockAndCreatesOrder_InOneTransaction()
        {
            var productId = await CreateProductAsync(price: 40m, stock: 10);

            var payload = new
            {
                customerName = "Dana-" + Guid.NewGuid().ToString("N").Substring(0, 4),
                items = new[] { new { productId, quantity = 3 } }
            };

            var placeResp = await _client.PostAsJsonAsync("/api/orders/place", payload);
            Assert.Equal(HttpStatusCode.Created, placeResp.StatusCode);

            var id = await ReadCreatedIdAsync(placeResp);
            Assert.NotEqual(Guid.Empty, id);

            var getOrder = await _client.GetAsync($"/api/orders/{id}");
            Assert.Equal(HttpStatusCode.OK, getOrder.StatusCode);
            var orderDto = await getOrder.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(120m, orderDto.GetProperty("totalAmount").GetDecimal());

            var getProduct = await _client.GetAsync($"/api/products/{productId}");
            Assert.Equal(HttpStatusCode.OK, getProduct.StatusCode);
            var productDto = await getProduct.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(7, productDto.GetProperty("stock").GetInt32());
        }

        [Fact]
        public async Task Place_InsufficientStock_Returns_400()
        {
            var productId = await CreateProductAsync(price: 40m, stock: 2);

            var payload = new
            {
                customerName = "Eve",
                items = new[] { new { productId, quantity = 5 } }
            };

            var resp = await _client.PostAsJsonAsync("/api/orders/place", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task GetAll_ReturnsPagedEnvelopeWithCreatedOrders()
        {
            var productId = await CreateProductAsync(price: 20m);
            var marker = "LIST-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var orderPayload = new
            {
                customerName = marker,
                items = new[] { new { productId, quantity = 2 } }
            };
            var createResp = await _client.PostAsJsonAsync("/api/orders", orderPayload);
            Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

            var listResp = await _client.GetAsync("/api/orders?page=1&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

            var envelope = await listResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Object, envelope.ValueKind);
            Assert.True(envelope.GetProperty("totalCount").GetInt32() >= 1);
            Assert.Equal(1, envelope.GetProperty("page").GetInt32());
            Assert.Equal(100, envelope.GetProperty("pageSize").GetInt32());

            var items = envelope.GetProperty("items");
            Assert.Equal(JsonValueKind.Array, items.ValueKind);

            JsonElement? mine = null;
            foreach (var el in items.EnumerateArray())
            {
                if (el.GetProperty("customerName").GetString() == marker)
                {
                    mine = el;
                    break;
                }
            }

            Assert.NotNull(mine);
            Assert.Equal(40m, mine!.Value.GetProperty("totalAmount").GetDecimal());
            Assert.Equal(1, mine.Value.GetProperty("items").GetArrayLength());
        }
    }
}
