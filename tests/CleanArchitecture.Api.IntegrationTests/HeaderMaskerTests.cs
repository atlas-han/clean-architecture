using CleanArchitecture.Api.Logging;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class HeaderMaskerTests
    {
        [Theory]
        [InlineData("Authorization")]
        [InlineData("Proxy-Authorization")]
        [InlineData("Cookie")]
        [InlineData("Set-Cookie")]
        [InlineData("X-Api-Key")]
        public void Mask_RedactsSensitiveHeaders(string name)
        {
            Assert.True(HeaderMasker.IsSensitive(name));
            Assert.Equal("***", HeaderMasker.Mask(name, "the-secret-value"));
        }

        [Theory]
        [InlineData("authorization")]
        [InlineData("X-API-KEY")]
        [InlineData("set-cookie")]
        public void Mask_IsCaseInsensitive(string name)
        {
            // HTTP header names are case-insensitive — masking must follow suit.
            Assert.Equal("***", HeaderMasker.Mask(name, "the-secret-value"));
        }

        [Theory]
        [InlineData("User-Agent", "Mozilla/5.0")]
        [InlineData("Content-Type", "application/json")]
        [InlineData("X-Debug-Tag", "trace-me")]
        [InlineData("X-Request-Id", "req-123")]
        public void Mask_LeavesNonSensitiveHeadersUntouched(string name, string value)
        {
            Assert.False(HeaderMasker.IsSensitive(name));
            Assert.Equal(value, HeaderMasker.Mask(name, value));
        }
    }
}
