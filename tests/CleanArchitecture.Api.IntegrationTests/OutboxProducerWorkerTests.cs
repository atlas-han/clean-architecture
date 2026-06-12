using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.Events;
using CleanArchitecture.Domain.ValueObjects;
using CleanArchitecture.Infrastructure.BackgroundServices;
using CleanArchitecture.Infrastructure.Messaging;
using CleanArchitecture.Infrastructure.Outbox;
using CleanArchitecture.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    // Drives the single outbox producer worker's drain pass (ProduceBatchAsync) directly — no timer —
    // against an isolated InMemory database + a test publisher, proving publish / mark-processed /
    // retry-on-failure behaviour deterministically.
    public class OutboxProducerWorkerTests
    {
        private sealed class FixedDateTime : IDateTime
        {
            public DateTime UtcNow { get; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        }

        private sealed class FakeMaintenanceState : IMaintenanceState
        {
            public bool IsStopped { get; set; }
            public void Stop() => IsStopped = true;
            public void Resume() => IsStopped = false;
        }

        private sealed class RecordingPublisher : IEventPublisher
        {
            public List<(string Type, string Key, string Payload)> Published { get; } =
                new List<(string, string, string)>();

            public Task PublishAsync(string eventType, string key, string payload, CancellationToken cancellationToken)
            {
                Published.Add((eventType, key, payload));
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingPublisher : IEventPublisher
        {
            public Task PublishAsync(string eventType, string key, string payload, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("broker down");
        }

        // Fails its first failTimes calls, then succeeds — models a transient broker outage so the
        // retry path can be exercised without tripping the dead-letter cap.
        private sealed class FlakyPublisher : IEventPublisher
        {
            private readonly int _failTimes;
            private int _calls;

            public FlakyPublisher(int failTimes)
            {
                _failTimes = failTimes;
            }

            public List<(string Type, string Key, string Payload)> Published { get; } =
                new List<(string, string, string)>();

            public Task PublishAsync(string eventType, string key, string payload, CancellationToken cancellationToken)
            {
                _calls++;
                if (_calls <= _failTimes)
                    throw new InvalidOperationException("transient");

                Published.Add((eventType, key, payload));
                return Task.CompletedTask;
            }
        }

        // A publisher whose PublishAsync blocks mid-flight until the test releases it, and which
        // records whether the token it was handed can be cancelled. Lets a test drive a shutdown
        // *while a batch is in flight* and prove the publish runs to completion on an uncancellable
        // token rather than being abandoned.
        private sealed class GatedPublisher : IEventPublisher
        {
            private readonly TaskCompletionSource<bool> _entered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _release =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Entered => _entered.Task;
            public bool ReceivedTokenCanBeCanceled { get; private set; }
            public List<(string Type, string Key, string Payload)> Published { get; } =
                new List<(string, string, string)>();

            public void Release() => _release.TrySetResult(true);

            public async Task PublishAsync(string eventType, string key, string payload, CancellationToken cancellationToken)
            {
                ReceivedTokenCanBeCanceled = cancellationToken.CanBeCanceled;
                _entered.TrySetResult(true);
                await _release.Task;
                Published.Add((eventType, key, payload));
            }
        }

        // Captures the formatted message + level of every log call so a test can assert the
        // success log fires (the failure paths already log; only the success path was missing).
        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } =
                new List<(LogLevel, string)>();

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new NullScope();
                public void Dispose() { }
            }
        }

        private static ServiceProvider BuildProvider(IEventPublisher publisher, IDateTime dateTime)
        {
            // One database name per provider (computed once, not inside the lambda) so every scoped
            // context shares the same InMemory store — the seed scope and the worker scope must see
            // the same data.
            var dbName = Guid.NewGuid().ToString();

            var services = new ServiceCollection();
            services.AddSingleton(dateTime);
            services.AddSingleton(publisher);
            services.AddSingleton<ConvertDomainEventsToOutboxInterceptor>();
            services.AddDbContext<ApplicationDbContext>((sp, options) => options
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(sp.GetRequiredService<ConvertDomainEventsToOutboxInterceptor>()));
            return services.BuildServiceProvider();
        }

        // maxRetries defaults high so the pre-existing tests never trip the dead-letter cap; the
        // dead-letter tests pass a small value explicitly.
        private static OutboxProducerWorker NewWorker(ServiceProvider provider, int maxRetries = 100) =>
            new OutboxProducerWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeMaintenanceState(),
                NullLogger<OutboxProducerWorker>.Instance,
                TimeSpan.FromSeconds(1),
                100,
                maxRetries);

        private static async Task SeedOrderAsync(ServiceProvider provider)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Orders.Add(new Order("Alice", new[] { new OrderItem(Guid.NewGuid(), "Widget", new Money(100m), 2) }));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        private static async Task<List<OutboxMessage>> ReadOutboxAsync(ServiceProvider provider)
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.OutboxMessages.ToListAsync();
        }

        [Fact]
        public async Task ProduceBatchAsync_PublishesPendingMessages_AndMarksThemProcessed()
        {
            var publisher = new RecordingPublisher();
            using var provider = BuildProvider(publisher, new FixedDateTime());
            await SeedOrderAsync(provider);

            var published = await NewWorker(provider).ProduceBatchAsync(CancellationToken.None);

            Assert.Equal(1, published);
            var sent = Assert.Single(publisher.Published);
            Assert.Equal(nameof(OrderPlacedDomainEvent), sent.Type);

            var message = Assert.Single(await ReadOutboxAsync(provider));
            Assert.Equal(sent.Key, message.AggregateId.ToString());
            Assert.NotNull(message.ProcessedOnUtc);
            Assert.Null(message.Error);
        }

        [Fact]
        public async Task ProduceBatchAsync_OnPublishSuccess_LogsPublishedWithOutboxId()
        {
            var publisher = new RecordingPublisher();
            using var provider = BuildProvider(publisher, new FixedDateTime());
            await SeedOrderAsync(provider);

            var logger = new RecordingLogger<OutboxProducerWorker>();
            var worker = new OutboxProducerWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeMaintenanceState(),
                logger,
                TimeSpan.FromSeconds(1),
                100,
                100);

            await worker.ProduceBatchAsync(CancellationToken.None);

            var message = Assert.Single(await ReadOutboxAsync(provider));
            Assert.Single(logger.Entries,
                e => e.Level == LogLevel.Information && e.Message.Contains(message.Id.ToString()));
        }

        [Fact]
        public async Task ProduceBatchAsync_OnPublishFailure_LeavesRowUnprocessedAndRecordsError()
        {
            using var provider = BuildProvider(new ThrowingPublisher(), new FixedDateTime());
            await SeedOrderAsync(provider);

            var published = await NewWorker(provider).ProduceBatchAsync(CancellationToken.None);

            Assert.Equal(0, published);
            var message = Assert.Single(await ReadOutboxAsync(provider));
            Assert.Null(message.ProcessedOnUtc);
            Assert.Equal("broker down", message.Error);
        }

        [Fact]
        public async Task ProduceBatchAsync_DoesNotRepublishAlreadyProcessedMessages()
        {
            var publisher = new RecordingPublisher();
            using var provider = BuildProvider(publisher, new FixedDateTime());
            await SeedOrderAsync(provider);

            await NewWorker(provider).ProduceBatchAsync(CancellationToken.None);
            var secondPass = await NewWorker(provider).ProduceBatchAsync(CancellationToken.None);

            Assert.Equal(0, secondPass);
            Assert.Single(publisher.Published);
        }

        [Fact]
        public async Task ProduceBatchAsync_WithNothingPending_PublishesNothing()
        {
            var publisher = new RecordingPublisher();
            using var provider = BuildProvider(publisher, new FixedDateTime());

            var published = await NewWorker(provider).ProduceBatchAsync(CancellationToken.None);

            Assert.Equal(0, published);
            Assert.Empty(publisher.Published);
        }

        [Fact]
        public async Task ProduceBatchAsync_OnRepeatedFailure_DeadLettersOnceMaxAttemptsReached()
        {
            var dateTime = new FixedDateTime();
            using var provider = BuildProvider(new ThrowingPublisher(), dateTime);
            await SeedOrderAsync(provider);

            var worker = NewWorker(provider, maxRetries: 2);

            await worker.ProduceBatchAsync(CancellationToken.None);
            var afterFirst = Assert.Single(await ReadOutboxAsync(provider));
            Assert.Equal(1, afterFirst.Attempts);
            Assert.Null(afterFirst.DeadLetteredOnUtc); // below the cap → still retryable

            await worker.ProduceBatchAsync(CancellationToken.None);
            var afterSecond = Assert.Single(await ReadOutboxAsync(provider));
            Assert.Equal(2, afterSecond.Attempts);
            Assert.Equal(dateTime.UtcNow, afterSecond.DeadLetteredOnUtc); // cap reached → quarantined
            Assert.Equal("broker down", afterSecond.Error);
            Assert.Null(afterSecond.ProcessedOnUtc);
        }

        [Fact]
        public async Task ProduceBatchAsync_DeadLetteredMessage_IsNotPickedUpAgain()
        {
            using var provider = BuildProvider(new ThrowingPublisher(), new FixedDateTime());
            await SeedOrderAsync(provider);

            var worker = NewWorker(provider, maxRetries: 1);

            await worker.ProduceBatchAsync(CancellationToken.None); // one failure → immediately dead-lettered
            var dead = Assert.Single(await ReadOutboxAsync(provider));
            Assert.NotNull(dead.DeadLetteredOnUtc);
            Assert.Equal(1, dead.Attempts);

            // A later pass must skip the quarantined row entirely — no further publish attempt.
            var published = await worker.ProduceBatchAsync(CancellationToken.None);
            Assert.Equal(0, published);

            var still = Assert.Single(await ReadOutboxAsync(provider));
            Assert.Equal(1, still.Attempts); // not incremented again — it was never re-attempted
        }

        [Fact]
        public async Task ProduceBatchAsync_TransientFailureBelowCap_RecoversWithoutDeadLettering()
        {
            var publisher = new FlakyPublisher(failTimes: 1);
            using var provider = BuildProvider(publisher, new FixedDateTime());
            await SeedOrderAsync(provider);

            var worker = NewWorker(provider, maxRetries: 3);

            await worker.ProduceBatchAsync(CancellationToken.None); // attempt 1 fails
            var afterFail = Assert.Single(await ReadOutboxAsync(provider));
            Assert.Equal(1, afterFail.Attempts);
            Assert.Null(afterFail.DeadLetteredOnUtc);
            Assert.Null(afterFail.ProcessedOnUtc);

            var published = await worker.ProduceBatchAsync(CancellationToken.None); // attempt 2 succeeds
            Assert.Equal(1, published);

            var done = Assert.Single(await ReadOutboxAsync(provider));
            Assert.NotNull(done.ProcessedOnUtc);
            Assert.Null(done.DeadLetteredOnUtc);
            Assert.Null(done.Error);
            Assert.Single(publisher.Published);
        }

        [Fact]
        public async Task Shutdown_LetsInFlightBatchFinish_WithoutCancellingThePublish()
        {
            var publisher = new GatedPublisher();
            using var provider = BuildProvider(publisher, new FixedDateTime());
            await SeedOrderAsync(provider);

            // Short poll so the first tick — and the gated publish — starts almost immediately.
            var worker = new OutboxProducerWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeMaintenanceState(),
                NullLogger<OutboxProducerWorker>.Instance,
                TimeSpan.FromMilliseconds(20),
                100,
                100);

            await worker.StartAsync(CancellationToken.None);

            // Block until the worker is mid-publish — the drain batch is now in flight.
            await publisher.Entered;

            // Request shutdown while the batch is in flight: base.StopAsync cancels the stoppingToken.
            // A graceful worker must still let the in-flight publish complete rather than abandon it.
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var stopTask = worker.StopAsync(stopCts.Token);

            publisher.Release();
            await stopTask;

            // The publish ran to completion despite the shutdown, and it was handed an uncancellable
            // token — proving the in-flight batch is decoupled from the stoppingToken.
            Assert.False(publisher.ReceivedTokenCanBeCanceled);
            Assert.Single(publisher.Published);
            var message = Assert.Single(await ReadOutboxAsync(provider));
            Assert.NotNull(message.ProcessedOnUtc);
        }

        [Fact]
        public async Task StartThenStop_EmitsLifecycleLogs()
        {
            using var provider = BuildProvider(new RecordingPublisher(), new FixedDateTime());

            var logger = new RecordingLogger<OutboxProducerWorker>();
            // Long poll so no drain tick fires during the test — we only assert the lifecycle logs.
            var worker = new OutboxProducerWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeMaintenanceState(),
                logger,
                TimeSpan.FromMinutes(5),
                100,
                100);

            await worker.StartAsync(CancellationToken.None);
            await worker.StopAsync(CancellationToken.None);

            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("started"));
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("stopping"));
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("stopped"));
        }
    }
}
