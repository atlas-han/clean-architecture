using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Api.Common
{
    public static class HttpContextExtensions
    {
        // The 32-hex W3C trace-id (§4.4) used as the response `traceId`. Kept
        // consistent with the access log's traceID field (RequestLoggingMiddleware)
        // so a response body can be correlated to its server logs by the same id.
        public static string GetTraceId(this HttpContext context)
        {
            var traceId = Activity.Current?.TraceId.ToString();
            if (!string.IsNullOrEmpty(traceId) && traceId != "00000000000000000000000000000000")
            {
                return traceId;
            }

            return context.TraceIdentifier;
        }
    }
}
