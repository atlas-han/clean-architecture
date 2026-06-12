using System;
using System.Collections.Generic;
using CleanArchitecture.Infrastructure;
using CleanArchitecture.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // AddInfrastructure wiring for the outbox → Kafka publisher: the Kafka-vs-logging decision and
    // its fail-fast guard, mirroring InfrastructureDependencyInjectionTests for the idempotency store.
    public class OutboxDependencyInjectionTests
    {
        [Fact]
        public void NoKafka_OutsideDevelopment_FailsFastAtStartup()
        {
            var services = new ServiceCollection();

            // Redis is configured so the earlier Redis guard passes and the Kafka guard is reached.
            var ex = Assert.Throws<InvalidOperationException>(() =>
                services.AddInfrastructure(
                    Config(("ConnectionStrings:Redis", "localhost:6379,abortConnect=false")),
                    Env(Environments.Production)));

            Assert.Contains("Kafka:BootstrapServers", ex.Message);
        }

        [Fact]
        public void NoKafka_InDevelopment_UsesLoggingFallback()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(Config(), Env(Environments.Development));

            using var provider = services.BuildServiceProvider();
            Assert.IsType<LoggingEventPublisher>(provider.GetRequiredService<IEventPublisher>());
        }

        [Fact]
        public void NoKafka_InLocal_UsesLoggingFallback()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(Config(), Env("Local"));

            using var provider = services.BuildServiceProvider();
            Assert.IsType<LoggingEventPublisher>(provider.GetRequiredService<IEventPublisher>());
        }

        [Fact]
        public void Kafka_Configured_UsesKafkaPublisher()
        {
            var services = new ServiceCollection();
            services.AddInfrastructure(
                Config(("Kafka:BootstrapServers", "localhost:9092")),
                Env(Environments.Development));

            using var provider = services.BuildServiceProvider();
            Assert.IsType<KafkaEventPublisher>(provider.GetRequiredService<IEventPublisher>());
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
