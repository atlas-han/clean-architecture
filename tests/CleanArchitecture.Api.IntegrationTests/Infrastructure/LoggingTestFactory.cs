using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Api.IntegrationTests.Infrastructure
{
    public sealed class LoggingTestFactory : WebApplicationFactory<Program>
    {
        public CapturingLoggerProvider Provider { get; } = new CapturingLoggerProvider(LogLevel.Debug);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Load the test-only ThrowingTestController (/api/_test/*) so header-logging
            // tests can drive a probe endpoint (e.g. one that emits Set-Cookie).
            builder.ConfigureServices(services =>
            {
                services.AddControllers()
                    .PartManager.ApplicationParts.Add(
                        new AssemblyPart(typeof(ThrowingTestController).Assembly));
            });
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddFilter("CleanArchitecture.Api.Middleware.RequestLoggingMiddleware", LogLevel.Debug);
                logging.AddProvider(Provider);
            });
        }
    }

    // Information-level host: the middleware must NOT capture request/response bodies (§14.6).
    public sealed class InfoLoggingTestFactory : WebApplicationFactory<Program>
    {
        public CapturingLoggerProvider Provider { get; } = new CapturingLoggerProvider(LogLevel.Information);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddProvider(Provider);
            });
        }
    }
}
