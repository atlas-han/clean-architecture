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

        // Single-resource payloads live under the SuccessResponse `data` field (§4.2).
        private static async Task<Guid> ReadCreatedIdAsync(HttpResponseMessage resp)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("data").GetProperty("id").GetGuid();
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

            var created = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            var id = created.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, id);
            Assert.Equal(orderPayload.customerName, created.GetProperty("customerName").GetString());
            Assert.Equal(150m, created.GetProperty("totalAmount").GetDecimal());

            var getResp = await _client.GetAsync($"/api/orders/{id}");
            Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

            var data = (await getResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            Assert.Equal(orderPayload.customerName, data.GetProperty("customerName").GetString());
            Assert.Equal(150m, data.GetProperty("totalAmount").GetDecimal());
            Assert.Equal(1, data.GetProperty("items").GetArrayLength());
        }

        [Fact]
        public async Task Create_InvalidPayload_Returns_400_With_FieldErrors()
        {
            var payload = new { customerName = "", items = new object[0] };

            var resp = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("VALIDATION_ERROR", body.GetProperty("error").GetProperty("code").GetString());
            Assert.True(body.GetProperty("error").TryGetProperty("details", out var details));
            Assert.Equal(JsonValueKind.Array, details.ValueKind);
        }

        [Fact]
        public async Task Create_InsufficientStock_Returns_422()
        {
            var productId = await CreateProductAsync(price: 40m, stock: 2);

            var payload = new
            {
                customerName = "Frank",
                items = new[] { new { productId, quantity = 5 } }
            };

            var resp = await _client.PostAsJsonAsync("/api/orders", payload);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
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
            var data = (await getResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            Assert.Equal(2, data.GetProperty("status").GetInt32());
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
            var data = (await getResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            Assert.Equal(1, data.GetProperty("status").GetInt32());
        }

        [Fact]
        public async Task Confirm_UnknownId_Returns_404()
        {
            var resp = await _client.PostAsync($"/api/orders/{Guid.NewGuid()}/confirm", null);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Confirm_CancelledOrder_Returns_422()
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
            Assert.Equal(HttpStatusCode.UnprocessableEntity, confirmResp.StatusCode);
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
            var orderData = (await getOrder.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            Assert.Equal(120m, orderData.GetProperty("totalAmount").GetDecimal());

            var getProduct = await _client.GetAsync($"/api/products/{productId}");
            Assert.Equal(HttpStatusCode.OK, getProduct.StatusCode);
            var productData = (await getProduct.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
            Assert.Equal(7, productData.GetProperty("stock").GetInt32());
        }

        [Fact]
        public async Task Place_InsufficientStock_Returns_422()
        {
            var productId = await CreateProductAsync(price: 40m, stock: 2);

            var payload = new
            {
                customerName = "Eve",
                items = new[] { new { productId, quantity = 5 } }
            };

            var resp = await _client.PostAsJsonAsync("/api/orders/place", payload);

            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        }

        [Fact]
        public async Task GetAll_ReturnsSuccessEnvelope_With_DataArray_And_Meta()
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

            var meta = envelope.GetProperty("meta");
            Assert.True(meta.GetProperty("totalCount").GetInt32() >= 1);
            Assert.Equal(1, meta.GetProperty("page").GetInt32());
            Assert.Equal(100, meta.GetProperty("pageSize").GetInt32());

            var data = envelope.GetProperty("data");
            Assert.Equal(JsonValueKind.Array, data.ValueKind);

            JsonElement? mine = null;
            foreach (var el in data.EnumerateArray())
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
