using System;
using CleanArchitecture.Infrastructure;
using CleanArchitecture.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Dedicated host for the transactional outbox → Kafka producer. The Api process only writes
// OutboxMessage rows inside the order/product transaction; this process drains them to Kafka out
// of band, so the producer can be deployed and scaled independently of request serving. Exactly one
// host must run the producer to avoid double-publishing: AddOutboxProcessing opts in here, and the
// Api deliberately does not call it. Shares the same Infrastructure wiring (DbContext, publisher,
// maintenance gate) as the Api via AddInfrastructure.
var builder = Host.CreateApplicationBuilder(args);

// Unified JSON console logging (§14.3), shared with the Api via Infrastructure so both
// hosts emit identical structured log lines to the same pipeline. Mirrors the Api host.
builder.Logging.AddUnifiedConsoleLogging();

builder.Services
    .AddInfrastructure(builder.Configuration, builder.Environment)
    .AddOutboxProcessing();

// Graceful shutdown: on SIGTERM/Ctrl+C give the in-flight outbox drain batch up to
// Outbox:ShutdownTimeoutSeconds (default 30s) to finish publishing before the host force-exits.
// OutboxProducerWorker stops scheduling new ticks the moment shutdown is requested and lets the
// current batch complete; this window bounds how long the host waits for it. Like the Api host, the
// timeout is configured here in the composition root; the value is keyed under Outbox alongside the
// producer's other knobs and parsed with the indexer + TryParse like them (not the Api's Binder style).
var shutdownTimeoutSeconds = int.TryParse(builder.Configuration["Outbox:ShutdownTimeoutSeconds"], out var parsedTimeout)
    && parsedTimeout > 0
        ? parsedTimeout
        : 30;
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(shutdownTimeoutSeconds));

builder.Build().Run();
