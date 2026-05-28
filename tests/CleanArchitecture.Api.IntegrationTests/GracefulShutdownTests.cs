using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class GracefulShutdownTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public GracefulShutdownTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public void HostOptions_ShutdownTimeout_Matches_Configuration_Default()
        {
            using var scope = _factory.Services.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<IOptions<HostOptions>>().Value;

            Assert.Equal(TimeSpan.FromSeconds(30), options.ShutdownTimeout);
        }

        [Fact]
        public async Task StopApplication_Fires_Stopping_And_Stopped_Events()
        {
            // CreateClient() ensures the server (and the host) is fully started.
            _ = _factory.CreateClient();

            var lifetime = _factory.Services.GetRequiredService<IHostApplicationLifetime>();

            var stoppingFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stoppedFired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var stoppingReg = lifetime.ApplicationStopping.Register(() => stoppingFired.TrySetResult(true));
            using var stoppedReg = lifetime.ApplicationStopped.Register(() => stoppedFired.TrySetResult(true));

            lifetime.StopApplication();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var stoppingResult = await Task.WhenAny(stoppingFired.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.Same(stoppingFired.Task, stoppingResult);

            var stoppedResult = await Task.WhenAny(stoppedFired.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.Same(stoppedFired.Task, stoppedResult);
        }
    }
}
