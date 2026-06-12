using System;
using System.Linq;
using System.Text.Json;
using Asp.Versioning;
using CleanArchitecture.Api.Filters;
using CleanArchitecture.Api.Http;
using CleanArchitecture.Api.Middleware;
using CleanArchitecture.Application;
using CleanArchitecture.Infrastructure;
using CleanArchitecture.Infrastructure.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Unified JSON console logging (§14.3), shared with the Worker via Infrastructure.
builder.Logging.AddUnifiedConsoleLogging();

// In Development/Local on macOS the dev cert + HTTP/2 + SslStream combination intermittently
// fails on GOAWAY flush during connection teardown ("Bad address" from Http2FrameWriter.WriteGoAwayAsync),
// spamming Kestrel error logs even though the response was already delivered. Pin local HTTPS
// to HTTP/1.1 via Kestrel's config-binding path so URL-bound endpoints honor it; production
// keeps the framework default (Http1AndHttp2).
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Local"))
{
    builder.Configuration["Kestrel:EndpointDefaults:Protocols"] = "Http1";
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// §7.4 step 3: lets DeadlinePropagationHandler read the inbound request's deadline so it can
// re-propagate X-Request-Deadline to downstream HttpClient calls. Attach the handler to any
// typed/named client with AddHttpClient(...).AddHttpMessageHandler<DeadlinePropagationHandler>().
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<DeadlinePropagationHandler>();

// Graceful shutdown: give in-flight requests up to Shutdown:Timeout (default 30s) to drain
// after SIGTERM / Ctrl+C before the host force-exits.
var shutdownTimeout = builder.Configuration.GetValue<TimeSpan?>("Shutdown:Timeout") ?? TimeSpan.FromSeconds(30);
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = shutdownTimeout);

// "application" liveness check; the "database" check is registered in AddInfrastructure.
builder.Services.AddHealthChecks()
    .AddCheck("application", () => HealthCheckResult.Healthy("Application is running"));

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiExceptionFilter>();
});

// URI API versioning per API design guide §5.1. Version is read from the URL segment
// (/api/v1/...) first, then the X-Api-Version header. v1.0 is assumed when unspecified, and
// supported versions are advertised back via the api-supported-versions response header.
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"));
})
.AddMvc();

// HSTS (Strict-Transport-Security) options per §9.1. UseHsts() is wired into the pipeline
// for non-development environments only (HSTS is meaningful solely over HTTPS).
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CleanArchitecture API",
        Version = "v1",
        Description = "ASP.NET Core 9 (C# 9 LangVersion) Clean Architecture sample."
    });
});

var app = builder.Build();

var lifetime = app.Lifetime;
var shutdownLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GracefulShutdown");
lifetime.ApplicationStopping.Register(() =>
    shutdownLogger.LogInformation("Application stopping: draining in-flight requests (timeout {shutdown_timeout_seconds}s)", shutdownTimeout.TotalSeconds));
lifetime.ApplicationStopped.Register(() =>
    shutdownLogger.LogInformation("Application stopped"));

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Local"))
{
    app.UseDeveloperExceptionPage();
}

// app.UseSwagger();
// app.UseSwaggerUI(c =>
// {
//     c.SwaggerEndpoint("/swagger/v1/swagger.json", "CleanArchitecture v1");
//     c.RoutePrefix = string.Empty;
// });

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Local"))
{
    // HSTS is only meaningful over HTTPS; excluded in dev/Local so local HTTP isn't pinned. (§9.1)
    app.UseHsts();
}

app.UseHttpsRedirection();

// Common security headers (nosniff / X-Frame-Options / X-XSS-Protection / CSP) on every response,
// placed before the gates below so maintenance/deadline short-circuits also carry them. (§9.1)
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseMiddleware<RequestLoggingMiddleware>();

// Maintenance gate: while stopped, returns 503 for everything except /health and the
// /admin/maintenance control endpoints. First gate after logging so a maintenance window
// short-circuits before any deadline logic, and rejected requests are still access-logged.
app.UseMiddleware<MaintenanceMiddleware>();

// Inside the logging middleware so a deadline fast-fail (504) is still captured in the access log.
app.UseMiddleware<DeadlinePropagationMiddleware>();

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description
            })
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

app.Run();

public partial class Program { }
