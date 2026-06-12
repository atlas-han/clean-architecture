using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Api.Http
{
    // §7.4 step 3: re-propagate the inbound request deadline to downstream service calls.
    // Attach to an HttpClient via AddHttpClient(...).AddHttpMessageHandler<DeadlinePropagationHandler>()
    // and it copies the current request's absolute X-Request-Deadline onto every outbound request —
    // the same auto-propagation pattern as traceparent (§4.4). The deadline is absolute (epoch ms),
    // so forwarding it unchanged lets the downstream service run its own step-1 fast-fail against the
    // shared budget. An already-present header is left untouched (explicit caller intent wins).
    public class DeadlinePropagationHandler : DelegatingHandler
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DeadlinePropagationHandler(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var context = _httpContextAccessor.HttpContext;

            if (context != null
                && context.Items.TryGetValue(DeadlinePropagationMiddleware.DeadlineItemKey, out var value)
                && value is DateTimeOffset deadline
                && !request.Headers.Contains(DeadlinePropagationMiddleware.DeadlineHeader))
            {
                request.Headers.TryAddWithoutValidation(
                    DeadlinePropagationMiddleware.DeadlineHeader,
                    deadline.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
