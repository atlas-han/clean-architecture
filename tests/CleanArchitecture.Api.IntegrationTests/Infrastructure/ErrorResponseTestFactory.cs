using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Api.IntegrationTests.Infrastructure
{
    public class ErrorResponseTestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            // Outside Development/Local the idempotency store requires ConnectionStrings:Redis
            // (AddInfrastructure fails fast otherwise). Supply one so this Production-env host boots;
            // the cache connects lazily and these tests never touch it (abortConnect=false keeps the
            // unused multiplexer from throwing on the absent server).
            builder.UseSetting("ConnectionStrings:Redis", "localhost:6379,abortConnect=false");
            builder.ConfigureServices(services =>
            {
                services.AddControllers()
                    .PartManager.ApplicationParts.Add(
                        new AssemblyPart(typeof(ThrowingTestController).Assembly));
            });
        }
    }
}
