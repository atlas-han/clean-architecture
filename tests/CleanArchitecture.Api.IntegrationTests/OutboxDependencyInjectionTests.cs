using System;
using System.Collections.Generic;
using CleanArchitecture.Infrastructure;
using CleanArchitecture.Infrastructure.BackgroundServices;
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

        [Fact]
        public void AddInfrastructure_RegistersOutboxProducer_ButDoesNotHostIt()
        {
            var config = Config();
            var services = new ServiceCollection();
            services.AddLogging();
            // Real hosts register IConfiguration in DI; resolving the hosted services below pulls it
            // (IMaintenanceState reads it), so register it here too.
            services.AddSingleton<IConfiguration>(config);
            services.AddInfrastructure(config, Env(Environments.Development));

            using var provider = services.BuildServiceProvider();

            // The worker is resolvable — a host can opt into running it, and tests can drive its
            // drain pass directly...
            Assert.NotNull(provider.GetService<OutboxProducerWorker>());
            // ...but AddInfrastructure must NOT host it. Only the dedicated Worker process drains the
            // outbox, so the Api (which calls only AddInfrastructure) never double-publishes.
            Assert.DoesNotContain(
                provider.GetServices<IHostedService>(),
                hosted => hosted is OutboxProducerWorker);
        }

        [Fact]
        public void AddOutboxProcessing_HostsTheOutboxProducer()
        {
            var config = Config();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(config);
            services.AddInfrastructure(config, Env(Environments.Development));
            services.AddOutboxProcessing();

            using var provider = services.BuildServiceProvider();

            // The dedicated Worker host opts in via AddOutboxProcessing, so the producer runs there.
            Assert.Contains(
                provider.GetServices<IHostedService>(),
                hosted => hosted is OutboxProducerWorker);
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
