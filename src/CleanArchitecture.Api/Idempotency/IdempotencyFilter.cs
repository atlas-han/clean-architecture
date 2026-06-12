using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CleanArchitecture.Api.Common;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CleanArchitecture.Api.Idempotency
{
    // Resource filter implementing the §7.1 Idempotency-Key contract for a single action:
    //   • no key            → pass through (the key is optional on this sample's endpoints)
    //   • key, never seen   → claim it (InProgress), run the action, cache a 2xx response
    //   • key, Completed     → replay the cached response verbatim (+ Idempotency-Replayed: true)
    //   • key, InProgress    → 409 CONFLICT (a concurrent request holds the key; §7.1 — 409, not 425)
    //   • key, diff payload  → 409 CONFLICT (same key reused with a different request body; §7.1 allows 409)
    // A resource filter (not an action filter) is used so the body can be fingerprinted before model
    // binding and the serialized response captured before it leaves the pipeline. Only 2xx responses
    // are cached; on any failure the claim is released so a legitimate retry isn't blocked (§7.4).
    public class IdempotencyFilter : IAsyncResourceFilter
    {
        public const string HeaderName = "Idempotency-Key";
        public const string ReplayedHeader = "Idempotency-Replayed";

        private readonly IIdempotencyStore _store;

        public IdempotencyFilter(IIdempotencyStore store)
        {
            _store = store;
        }

        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            var key = httpContext.Request.Headers[HeaderName].FirstOrDefault();

            // Optional on this sample's endpoints: no key → normal processing, no dedup.
            if (string.IsNullOrWhiteSpace(key))
            {
                await next();
                return;
            }

            var cancellationToken = httpContext.RequestAborted;
            var fingerprint = await ComputeFingerprintAsync(httpContext.Request);

            var existing = await _store.GetAsync(key, cancellationToken);
            if (existing != null)
            {
                if (existing.Fingerprint != fingerprint)
                {
                    context.Result = Conflict(httpContext,
                        "This Idempotency-Key was already used with a different request payload.");
                    return;
                }

                if (existing.Status == IdempotencyStatus.Completed && existing.Response != null)
                {
                    context.Result = BuildReplay(httpContext, existing.Response);
                    return;
                }

                context.Result = Conflict(httpContext,
                    "A request with this Idempotency-Key is already being processed.");
                return;
            }

            // Claim the key. If a concurrent request claimed it first, surface the in-progress conflict.
            if (!await _store.TryBeginAsync(key, fingerprint, cancellationToken))
            {
                context.Result = Conflict(httpContext,
                    "A request with this Idempotency-Key is already being processed.");
                return;
            }

            await ExecuteAndCaptureAsync(context, next, key, fingerprint);
        }

        // Runs the action with the response body buffered, then caches a 2xx response (or releases the
        // claim on failure) and writes the captured body through to the real response stream byte-for-byte.
        private async Task ExecuteAndCaptureAsync(ResourceExecutingContext context, ResourceExecutionDelegate next,
            string key, string fingerprint)
        {
            var httpContext = context.HttpContext;
            var originalBody = httpContext.Response.Body;
            using (var buffer = new MemoryStream())
            {
                httpContext.Response.Body = buffer;
                try
                {
                    await next();
                }
                catch
                {
                    // Unhandled throw (no exception filter claimed it): restore the stream and release
                    // the claim so a retry isn't blocked, then let it propagate unchanged.
                    httpContext.Response.Body = originalBody;
                    await TryReleaseAsync(key);
                    throw;
                }

                httpContext.Response.Body = originalBody;
                var response = httpContext.Response;

                if (response.StatusCode >= 200 && response.StatusCode < 300)
                {
                    buffer.Position = 0;
                    var bodyText = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
                    var snapshot = new IdempotencyResponse(
                        response.StatusCode,
                        response.ContentType,
                        bodyText,
                        response.Headers.Location.FirstOrDefault());
                    // Record the outcome with CancellationToken.None so a client disconnect can't leave
                    // the key stuck InProgress / uncached (§7.4).
                    await _store.CompleteAsync(key, fingerprint, snapshot, CancellationToken.None);
                }
                else
                {
                    // Non-success (e.g. 404/422 shaped by the exception filter): don't cache; release
                    // the claim so the client can correct and retry with the same key.
                    await TryReleaseAsync(key);
                }

                // Write the captured response through to the real stream byte-for-byte (no re-encoding).
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody);
            }
        }

        // Best-effort claim release: a failure here must not mask the real outcome, and it uses
        // CancellationToken.None so cleanup survives a client abort. A key we cannot release is
        // backstopped by the store's 24h TTL rather than staying stuck InProgress (§7.4).
        private async Task TryReleaseAsync(string key)
        {
            try
            {
                await _store.ReleaseAsync(key, CancellationToken.None);
            }
            catch
            {
                // Swallow: the 24h TTL will expire a key we couldn't release.
            }
        }

        // Stable hash over method + path + raw body so a retry of the same request matches, while a
        // reuse of the key with a different payload is detected (§7.1 — same key + different params).
        private static async Task<string> ComputeFingerprintAsync(HttpRequest request)
        {
            request.EnableBuffering();
            request.Body.Position = 0;
            string body;
            using (var reader = new StreamReader(request.Body, Encoding.UTF8, false, 1024, leaveOpen: true))
            {
                body = await reader.ReadToEndAsync();
            }
            request.Body.Position = 0;

            var material = request.Method + "\n" + request.Path + "\n" + body;
            using (var sha = SHA256.Create())
            {
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(material)));
            }
        }

        private static IActionResult Conflict(HttpContext httpContext, string message)
        {
            var body = ApiResult.Error(httpContext, ErrorCodes.Conflict, message);
            return new ObjectResult(body) { StatusCode = StatusCodes.Status409Conflict };
        }

        private static IActionResult BuildReplay(HttpContext httpContext, IdempotencyResponse response)
        {
            httpContext.Response.Headers[ReplayedHeader] = "true";
            if (!string.IsNullOrEmpty(response.Location))
            {
                httpContext.Response.Headers.Location = response.Location;
            }

            return new ContentResult
            {
                StatusCode = response.StatusCode,
                ContentType = response.ContentType,
                Content = response.Body
            };
        }
    }
}
