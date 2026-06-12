using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // Verifies the §9.1 common security headers are attached to every response by
    // SecurityHeadersMiddleware — on the versioned API surface and on non-/api endpoints alike.
    public class SecurityHeadersTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;

        public SecurityHeadersTests(WebApplicationFactory<Program> factory)
        {
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
        }

        [Fact]
        public async Task ApiResponse_CarriesSecurityHeaders()
        {
            var resp = await _client.GetAsync("/api/v1/products");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            AssertSecurityHeaders(resp);
        }

        [Fact]
        public async Task NonApiResponse_CarriesSecurityHeaders()
        {
            // Headers apply globally (§9.1) — not just under /api — so /health carries them too.
            var resp = await _client.GetAsync("/health");

            AssertSecurityHeaders(resp);
        }

        private static void AssertSecurityHeaders(HttpResponseMessage resp)
        {
            Assert.Equal("nosniff", Single(resp, "X-Content-Type-Options"));
            Assert.Equal("SAMEORIGIN", Single(resp, "X-Frame-Options"));
            Assert.Equal("1; mode=block", Single(resp, "X-XSS-Protection"));
            Assert.Equal("default-src 'self'; frame-ancestors 'self'", Single(resp, "Content-Security-Policy"));
        }

        private static string Single(HttpResponseMessage resp, string name)
        {
            Assert.True(resp.Headers.TryGetValues(name, out var values), name + " header is missing");
            return values.Single();
        }
    }
}
