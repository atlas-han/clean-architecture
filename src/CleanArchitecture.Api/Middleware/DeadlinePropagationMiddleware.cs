using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Api.Common;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Api.Middleware
{
    // Propagates an absolute request deadline across service hops (API design guide §7.4).
    // The X-Request-Deadline header carries the deadline as Unix epoch milliseconds.
    //   - Entry (step 1): compare the remaining budget against MinimumBudget; if it is already
    //     gone (or too thin to be worth starting), short-circuit with 504 DEADLINE_EXCEEDED so a
    //     downstream service never works for a caller that has already timed out ("zombie request").
    //   - Live budget (step 2): bound the request to the remaining time with a CancellationToken
    //     (HttpContext.GetRequestCancellationToken()) that handlers/EF Core observe, so in-flight
    //     work is cancelled when the deadline elapses. The deadline itself is stashed in
    //     HttpContext.Items[DeadlineItemKey] so downstream consumers/diagnostics can read it.
    // The header is optional: requests without it pass through untouched.
    public class DeadlinePropagationMiddleware
    {
        public const string DeadlineHeader = "X-Request-Deadline";
        public const string DeadlineItemKey = "RequestDeadline";
        public const string DeadlineTokenItemKey = "RequestDeadlineToken";

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

                await InvokeWithDeadlineAsync(context, deadline);
                return;
            }

            await _next(context);
        }

        // §7.4 step 2: bound the request to the remaining budget. The token is linked off
        // RequestAborted (so a client disconnect still cancels) and fires on its own after the
        // budget elapses. RequestAborted itself is deliberately NOT replaced — the timer firing
        // is not a client disconnect, so the resulting 504 can still be written to a connected
        // client. Handlers reach this token via HttpContext.GetRequestCancellationToken().
        private async Task InvokeWithDeadlineAsync(HttpContext context, DateTimeOffset deadline)
        {
            using (var deadlineCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted))
            {
                // Re-read now: if the budget slipped to <= 0 between the entry gate and here (e.g. a
                // GC pause), cancel immediately rather than throwing on a negative CancelAfter.
                var remaining = deadline - DateTimeOffset.UtcNow;
                deadlineCts.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                context.Items[DeadlineItemKey] = deadline;
                context.Items[DeadlineTokenItemKey] = deadlineCts.Token;
                await _next(context);
            }
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

    public static class DeadlineHttpContextExtensions
    {
        // The deadline-bounded cancellation token for the current request (§7.4 step 2), or
        // RequestAborted when no X-Request-Deadline was supplied. Pass it to ISender.Send so
        // handler/EF Core work is cancelled the moment the budget is exhausted.
        public static CancellationToken GetRequestCancellationToken(this HttpContext context)
        {
            if (context.Items.TryGetValue(DeadlinePropagationMiddleware.DeadlineTokenItemKey, out var value)
                && value is CancellationToken token)
            {
                return token;
            }

            return context.RequestAborted;
        }
    }
}
