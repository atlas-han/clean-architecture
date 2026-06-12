using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using CleanArchitecture.Api.Common;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace CleanArchitecture.Api.Middleware
{
    // Short-circuits traffic with 503 + Retry-After while the service is in a maintenance
    // window (IMaintenanceState.IsStopped), using the standard §4.3 ErrorResponse envelope.
    // Health checks and the maintenance control endpoints are always exempt — otherwise an
    // operator could not resume the service and liveness probes would fail, causing the
    // orchestrator to kill the instance.
    public class MaintenanceMiddleware
    {
        private const int DefaultRetryAfterSeconds = 120;

        // Web defaults (camelCase) so the hand-written body matches the casing MVC uses for
        // the same ErrorResponse envelope elsewhere.
        private static readonly JsonSerializerOptions SerializerOptions =
            new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly RequestDelegate _next;
        private readonly IMaintenanceState _maintenance;
        private readonly int _retryAfterSeconds;

        public MaintenanceMiddleware(RequestDelegate next, IMaintenanceState maintenance, IConfiguration configuration)
        {
            _next = next;
            _maintenance = maintenance;
            _retryAfterSeconds = configuration.GetValue<int?>("Maintenance:RetryAfterSeconds") ?? DefaultRetryAfterSeconds;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!_maintenance.IsStopped || IsExempt(context.Request.Path))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = _retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            context.Response.ContentType = "application/json";

            var body = ApiResult.Error(context, ErrorCodes.ServiceUnavailable,
                "Service is under maintenance. Please retry later.");
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, SerializerOptions));
        }

        // Paths that must keep working during a maintenance window: liveness/readiness
        // probes and the control plane that flips the switch back off.
        private static bool IsExempt(PathString path)
        {
            return path.StartsWithSegments("/health")
                || path.StartsWithSegments("/admin/maintenance");
        }
    }
}
