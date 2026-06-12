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

builder.Build().Run();
