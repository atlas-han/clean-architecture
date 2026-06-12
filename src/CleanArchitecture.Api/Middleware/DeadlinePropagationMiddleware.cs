using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CleanArchitecture.Api.Common;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Api.Middleware
{
    // Propagates an absolute request deadline across service hops (API design guide §7.4).
    // The X-Request-Deadline header carries the deadline as Unix epoch milliseconds. On entry
    // the gateway compares the remaining budget against MinimumBudget and, if it is already
    // gone (or too thin to be worth starting), short-circuits with 504 DEADLINE_EXCEEDED so a
    // downstream service never does work for a caller that has already timed out ("zombie request").
    // A live deadline is stashed in HttpContext.Items[DeadlineItemKey] so handlers/DB calls can
    // bound themselves to it (guide §7.4 step 2/3). The header is optional: requests without it
    // pass through untouched.
    public class DeadlinePropagationMiddleware
    {
        public const string DeadlineHeader = "X-Request-Deadline";
        public const string DeadlineItemKey = "RequestDeadline";

        // Below this, the remaining budget is too small to outlast network/handler latency, so
        // we fail fast instead of forwarding (guide §7.4 — 10~50ms margin, middleware uses 50ms).
        private static readonly TimeSpan MinimumBudget = TimeSpan.FromMilliseconds(50);

        private readonly RequestDelegate _next;

        public DeadlinePropagationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var raw = context.Request.Headers[DeadlineHeader].FirstOrDefault();

            // No header → feature is opt-in; malformed value → ignore rather than reject (the
            // header is a hint from an upstream hop, not client input we should 400 on).
            if (!string.IsNullOrEmpty(raw)
                && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochMs))
            {
                var deadline = DateTimeOffset.FromUnixTimeMilliseconds(epochMs);

                if (deadline - DateTimeOffset.UtcNow < MinimumBudget)
                {
                    await WriteDeadlineExceededAsync(context);
                    return;
                }

                // Live budget: expose the deadline so downstream work can bound itself to it.
                context.Items[DeadlineItemKey] = deadline;
            }

            await _next(context);
        }

        private static Task WriteDeadlineExceededAsync(HttpContext context)
        {
            // Defensive: this middleware writes before anything else touches the body, so the
            // response should never have started here. Guard anyway so a future middleware that
            // starts the response earlier can't turn the fast-fail into a thrown exception.
            if (context.Response.HasStarted)
            {
                return Task.CompletedTask;
            }

            // Same §4.3 ErrorResponse envelope the ApiExceptionFilter emits (traceId + timestamp +
            // error{code,message}); built here directly because the short-circuit happens before MVC.
            var body = ApiResult.Error(context, ErrorCodes.DeadlineExceeded,
                "The request deadline (X-Request-Deadline) was exceeded before processing could start. Retry with a fresh deadline.");

            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return context.Response.WriteAsJsonAsync(body);
        }
    }
}
