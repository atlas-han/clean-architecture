using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Infrastructure.Messaging;
using CleanArchitecture.Infrastructure.Outbox;
using CleanArchitecture.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.BackgroundServices
{
    // The Producer side of the transactional outbox, and the ONLY component that talks to Kafka.
    // The request path just writes OutboxMessage rows inside the order/product transaction
    // (ConvertDomainEventsToOutboxInterceptor) and returns; this single background worker drains
    // them out of band, so request latency and DB load never wait on the broker. Skips ticks
    // during maintenance.
    public sealed class OutboxProducerWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMaintenanceState _maintenance;
        private readonly ILogger<OutboxProducerWorker> _logger;
        private readonly TimeSpan _pollInterval;
        private readonly int _batchSize;
        private readonly int _maxRetries;

        public OutboxProducerWorker(
            IServiceScopeFactory scopeFactory,
            IMaintenanceState maintenance,
            ILogger<OutboxProducerWorker> logger,
            TimeSpan pollInterval,
            int batchSize,
            int maxRetries)
        {
            _scopeFactory = scopeFactory;
            _maintenance = maintenance;
            _logger = logger;
            _pollInterval = pollInterval;
            _batchSize = batchSize;
            _maxRetries = maxRetries;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OutboxProducerWorker started; polling every {poll_interval}", _pollInterval);
            try
            {
                using (var timer = new PeriodicTimer(_pollInterval))
                {
                    while (await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        if (_maintenance.IsStopped)
                        {
                            _logger.LogInformation("OutboxProducerWorker tick skipped: maintenance mode is active");
                            continue;
                        }

                        try
                        {
                            // Graceful shutdown: once a drain pass has started, let it run to
                            // completion even if shutdown is requested mid-batch — pass an
                            // uncancellable token so an in-flight publish is never abandoned
                            // half-way. New ticks still stop because the next WaitForNextTickAsync
                            // observes the cancelled stoppingToken; HostOptions.ShutdownTimeout
                            // bounds how long the host waits for this pass to finish.
                            await ProduceBatchAsync(CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            // A tick-level failure (e.g. DB unreachable) must not kill the worker;
                            // log and retry next interval. Per-message failures are handled inside.
                            _logger.LogError(ex, "OutboxProducerWorker tick failed; will retry next interval");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown when the stopping token cancels WaitForNextTickAsync.
            }
        }

        // Graceful shutdown hook. The host (on SIGTERM/Ctrl+C or a programmatic stop) drives this:
        // base.StopAsync cancels the stoppingToken — which halts new ticks — and then waits for
        // ExecuteAsync to return, i.e. for any in-flight drain batch to finish publishing, bounded
        // by HostOptions.ShutdownTimeout. The log lines bracket the drain so operators can see the
        // worker shut down cleanly rather than being force-killed mid-batch.
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("OutboxProducerWorker stopping: no new ticks; finishing in-flight batch then exiting");
            await base.StopAsync(cancellationToken);
            _logger.LogInformation("OutboxProducerWorker stopped");
        }

        // One drain pass: publish the oldest pending messages in a single pipelined batch, then mark
        // each processed on success or record the error on failure (left unprocessed → retried next
        // tick, so delivery is at-least-once). A row that keeps failing is dead-lettered once its
        // attempt count reaches _maxRetries, so a poison message can't be retried forever. Public and
        // scope-managed so tests can drive it deterministically without spinning the timer loop.
        // Returns the number of messages published this pass.
        public async Task<int> ProduceBatchAsync(CancellationToken cancellationToken)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
                var dateTime = scope.ServiceProvider.GetRequiredService<IDateTime>();

                // Pending = unpublished AND not dead-lettered; quarantined poison messages are
                // skipped so they aren't retried forever (matches the filtered index predicate).
                List<OutboxMessage> messages = await db.Set<OutboxMessage>()
                    .Where(m => m.ProcessedOnUtc == null && m.DeadLetteredOnUtc == null)
                    .OrderBy(m => m.OccurredOnUtc)
                    .ThenBy(m => m.Id)
                    .Take(_batchSize)
                    .ToListAsync(cancellationToken);

                if (messages.Count == 0)
                    return 0;

                // Hand the whole batch to the publisher in one pipelined call instead of awaiting a
                // broker round-trip per message — that is what lets the drain keep up at thousands of
                // events/sec. Envelopes are built in drain order (OccurredOnUtc, Id) and the results
                // come back in the same order, so results[i] is the outcome for messages[i]; preserving
                // that order is also what keeps per-aggregate event order intact on the Kafka side.
                var envelopes = new List<EventEnvelope>(messages.Count);
                foreach (OutboxMessage message in messages)
                {
                    envelopes.Add(new EventEnvelope(
                        message.Type,
                        message.AggregateId.ToString(),
                        message.Content,
                        message.Id.ToString()));
                }

                IReadOnlyList<PublishResult> results = await publisher.PublishBatchAsync(envelopes, cancellationToken);

                var published = 0;
                for (var i = 0; i < messages.Count; i++)
                {
                    OutboxMessage message = messages[i];
                    PublishResult result = results[i];

                    if (result.Succeeded)
                    {
                        message.ProcessedOnUtc = dateTime.UtcNow;
                        message.Error = null;
                        published++;

                        _logger.LogInformation(
                            "Published outbox message {outbox_id} (type {event_type})",
                            message.Id, message.Type);
                    }
                    else
                    {
                        // Leave ProcessedOnUtc null so this row is retried next tick; one bad
                        // message must not block the rest of the batch.
                        message.Attempts++;
                        message.Error = result.Error;

                        if (message.Attempts >= _maxRetries)
                        {
                            // Poison message: stop retrying so it can't be reprocessed forever.
                            // Quarantine it by stamping DeadLetteredOnUtc — the drain query filters
                            // these out, but the row stays in the table (with its Error + Attempts)
                            // as a dead-letter record for inspection / manual replay.
                            message.DeadLetteredOnUtc = dateTime.UtcNow;
                            _logger.LogError(
                                "Dead-lettered outbox message {outbox_id} (type {event_type}) after {attempts} failed attempts: {error}",
                                message.Id, message.Type, message.Attempts, message.Error);
                        }
                        else
                        {
                            _logger.LogError(
                                "Failed to publish outbox message {outbox_id} (type {event_type}); attempt {attempts} of {max_attempts}, will retry: {error}",
                                message.Id, message.Type, message.Attempts, _maxRetries, message.Error);
                        }
                    }
                }

                await db.SaveChangesAsync(cancellationToken);
                return published;
            }
        }
    }
}
