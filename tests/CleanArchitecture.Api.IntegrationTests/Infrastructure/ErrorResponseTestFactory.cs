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
            builder.ConfigureServices(services =>
            {
                services.AddControllers()
                    .PartManager.ApplicationParts.Add(
                        new AssemblyPart(typeof(ThrowingTestController).Assembly));
            });
        }
    }
}
