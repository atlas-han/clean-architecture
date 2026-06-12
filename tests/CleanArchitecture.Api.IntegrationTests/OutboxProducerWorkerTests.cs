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
    }
}
