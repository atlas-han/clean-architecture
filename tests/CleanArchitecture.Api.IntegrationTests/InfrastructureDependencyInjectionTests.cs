using System;
using System.Collections.Generic;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure;
using CleanArchitecture.Infrastructure.Idempotency;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // AddInfrastructure wiring for the §7.1 idempotency store: the Redis-vs-in-memory decision and
    // its fail-fast guard (B), and the configurable key lifetime (C). Exercised directly against a
    // ServiceCollection + fake IHostEnvironment so it is independent of the WebApplicationFactory's
    // hosting environment.
    public class InfrastructureDependencyInjectionTests
    {
        // --- B: fail fast outside Development/Local when Redis is not configured ---

        [Fact]
        public void NoRedis_OutsideDevelopment_FailsFastAtStartup()
        {
            var services = new ServiceCollection();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                services.AddInfrastructure(Config(), Env(Environments.Production)));

            Assert.Contains("ConnectionStrings:Redis", ex.Message);
        }

        [Fact]
        public void NoRedis_InDevelopment_UsesInMemoryFallback()
        {
            var services = new ServiceCollection();

            services.AddInfrastructure(Config(), Env(Environments.Development));

            using var provider = services.BuildServiceProvider();
            Assert.NotNull(provider.GetRequiredService<IDistributedCache>());
            Assert.NotNull(provider.GetRequiredService<IIdempotencyStore>());
        }

        [Fact]
        public void NoRedis_InLocal_UsesInMemoryFallback()
        {
            var services = new ServiceCollection();

            services.AddInfrastructure(Config(), Env("Local"));

            using var provider = services.BuildServiceProvider();
            Assert.NotNull(provider.GetRequiredService<IDistributedCache>());
            Assert.NotNull(provider.GetRequiredService<IIdempotencyStore>());
        }

        [Fact]
        public void Redis_InProduction_DoesNotFailFast()
        {
            var services = new ServiceCollection();

            var ex = Record.Exception(() => services.AddInfrastructure(
                Config(("ConnectionStrings:Redis", "localhost:6379,abortConnect=false")),
                Env(Environments.Production)));

            Assert.Null(ex);
        }

        // --- C: configurable key lifetime (Idempotency:KeyLifetime), default 24h ---

        [Fact]
        public void KeyLifetime_DefaultsTo24Hours_WhenUnset()
        {
            var store = ResolveStore(Config());
            Assert.Equal(TimeSpan.FromHours(24), store.KeyLifetime);
        }

        [Fact]
        public void KeyLifetime_HonorsConfiguredValue()
        {
            var store = ResolveStore(Config(("Idempotency:KeyLifetime", "12:00:00")));
            Assert.Equal(TimeSpan.FromHours(12), store.KeyLifetime);
        }

        [Fact]
        public void KeyLifetime_FallsBackToDefault_WhenUnparseable()
        {
            var store = ResolveStore(Config(("Idempotency:KeyLifetime", "not-a-timespan")));
            Assert.Equal(TimeSpan.FromHours(24), store.KeyLifetime);
        }

        [Fact]
        public void KeyLifetime_FallsBackToDefault_WhenNonPositive()
        {
            var store = ResolveStore(Config(("Idempotency:KeyLifetime", "00:00:00")));
            Assert.Equal(TimeSpan.FromHours(24), store.KeyLifetime);
        }

        private static DistributedCacheIdempotencyStore ResolveStore(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddInfrastructure(configuration, Env(Environments.Development));
            using var provider = services.BuildServiceProvider();
            return Assert.IsType<DistributedCacheIdempotencyStore>(
                provider.GetRequiredService<IIdempotencyStore>());
        }

        private static IConfiguration Config(params (string Key, string Value)[] entries)
        {
            var dict = new Dictionary<string, string?>();
            foreach (var entry in entries)
            {
                dict[entry.Key] = entry.Value;
            }

            return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        }

        private static IHostEnvironment Env(string environmentName) =>
            new TestHostEnvironment { EnvironmentName = environmentName };

        private sealed class TestHostEnvironment : IHostEnvironment
        {
            public string ApplicationName { get; set; } = "IntegrationTests";
            public string EnvironmentName { get; set; } = Environments.Production;
            public string ContentRootPath { get; set; } = ".";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
