using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public HealthCheckTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        [Fact]
        public async Task Health_Returns_Healthy_With_Database_And_Application_Checks()
        {
            var resp = await _client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Healthy", json.GetProperty("status").GetString());

            var names = json.GetProperty("checks")
                .EnumerateArray()
                .Select(c => c.GetProperty("name").GetString())
                .ToList();

            Assert.Contains("database", names);
            Assert.Contains("application", names);
        }
    }
}
