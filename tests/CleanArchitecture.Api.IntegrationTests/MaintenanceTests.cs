using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class MaintenanceTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public MaintenanceTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        // The maintenance switch is a process-wide singleton shared across this class's
        // tests. Create the client first (which starts the host), then reset the switch to a
        // known "resumed" baseline so the test result is independent of execution order.
        private HttpClient CreateResumedClient()
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            _factory.Services.GetRequiredService<IMaintenanceState>().Resume();
            return client;
        }

        [Fact]
        public async Task Request_WhileStopped_Returns503_WithRetryAfter_AndErrorEnvelope()
        {
            var client = CreateResumedClient();

            var stop = await client.PostAsync("/admin/maintenance/stop", null);
            Assert.Equal(HttpStatusCode.OK, stop.StatusCode);

            var resp = await client.GetAsync("/api/v1/products");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            Assert.True(resp.Headers.TryGetValues("Retry-After", out _));

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("SERVICE_UNAVAILABLE", json.GetProperty("error").GetProperty("code").GetString());
            Assert.False(string.IsNullOrEmpty(json.GetProperty("traceId").GetString()));
        }

        [Fact]
        public async Task Health_RemainsAvailable_WhileStopped()
        {
            var client = CreateResumedClient();
            await client.PostAsync("/admin/maintenance/stop", null);

            var resp = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task Resume_RestoresNormalOperation()
        {
            var client = CreateResumedClient();

            await client.PostAsync("/admin/maintenance/stop", null);
            var stopped = await client.GetAsync("/api/v1/products");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, stopped.StatusCode);

            await client.PostAsync("/admin/maintenance/resume", null);
            var resumed = await client.GetAsync("/api/v1/products");
            Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        }

        [Fact]
        public async Task Status_Endpoint_ReflectsState()
        {
            var client = CreateResumedClient();

            var before = await (await client.GetAsync("/admin/maintenance"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(before.GetProperty("stopped").GetBoolean());

            await client.PostAsync("/admin/maintenance/stop", null);

            var after = await (await client.GetAsync("/admin/maintenance"))
                .Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(after.GetProperty("stopped").GetBoolean());
        }

        [Fact]
        public async Task ConfiguredEnabled_StopsApiFromStartup()
        {
            // A fresh host seeded with Maintenance:Enabled=true must reject traffic
            // immediately, without anyone calling the stop endpoint.
            using var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, cfg) =>
                {
                    cfg.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Maintenance:Enabled"] = "true"
                    });
                });
            });

            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            var resp = await client.GetAsync("/api/v1/products");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        }
    }
}
