using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Api.Middleware
{
    // Emits one access log per HTTP request using the unified API server logging
    // spec (API design guide §14.3/§14.4). Field names are a fixed contract with
    // the log pipeline — keep them exactly as-is, in snake_case (trace_id, request_id, latency_ms...).
    public class RequestLoggingMiddleware
    {
        private const int MaxBodyLength = 4096;
        private const string RequestIdHeader = "X-Request-Id";

        private static readonly string[] RequestIdHeaderCandidates =
        {
            "X-Request-Id",
            "X-Correlation-Id",
            "Correlation-Id",
            "CorrelationId",
            "Request-Id",
            "RequestId"
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger, IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestUuid = ResolveRequestUuid(context);
            var traceId = ResolveTraceId(context);
            var spanId = Activity.Current?.SpanId.ToHexString() ?? string.Empty;

            context.Response.Headers[RequestIdHeader] = requestUuid;

            var pathname = context.Request.Path.Value
                + (context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty);
            var method = context.Request.Method;
            var host = context.Request.Host.Value;
            var remoteAddr = context.Connection.RemoteIpAddress is { } remoteIp
                ? remoteIp + ":" + context.Connection.RemotePort
                : string.Empty;
            // §14.6: request/response bodies are off by default; captured only on debug paths.
            // When captured, PII fields inside the bodies are masked before logging (PiiMasker).
            var captureBodies = _logger.IsEnabled(LogLevel.Debug);
            var requestBody = captureBodies ? await ReadRequestBodyAsync(context.Request) : null;

            // Chunked requests carry no Content-Length; the buffered stream (debug path) knows the size.
            var reqBodyBytes = context.Request.ContentLength
                ?? (context.Request.Body.CanSeek ? context.Request.Body.Length : 0);

            // BeginScope keys = §14.3 log field names — every log emitted while handling
            // this request (handlers, filters, this access log) carries them automatically.
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["trace_id"] = traceId,
                ["span_id"] = spanId,
                ["request_id"] = requestUuid
            });

            // Response stream is wrapped so resBodyBytes is exact (§14.4).
            var originalBodyStream = context.Response.Body;
            await using var responseBuffer = new MemoryStream();
            context.Response.Body = responseBuffer;

            Exception? caught = null;
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                caught = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();

                string? responseBody = null;
                var resBodyBytes = responseBuffer.Length;
                try
                {
                    if (captureBodies)
                    {
                        responseBuffer.Position = 0;
                        using (var reader = new StreamReader(responseBuffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                        {
                            // Read in full here; PII masking runs on the whole body and
                            // truncation is applied afterwards so a cut never defeats masking.
                            responseBody = await reader.ReadToEndAsync();
                        }
                    }

                    // On an escaping exception, drop the partial body — the outer exception
                    // handler owns the response; flushing it first would corrupt the output.
                    if (caught == null)
                    {
                        responseBuffer.Position = 0;
                        await responseBuffer.CopyToAsync(originalBodyStream);
                    }
                }
                finally
                {
                    context.Response.Body = originalBodyStream;
                }

                var statusCode = context.Response.StatusCode;
                var level = DetermineLevel(statusCode, caught);
                var latencyMs = stopwatch.Elapsed.TotalMilliseconds;

                var state = new List<KeyValuePair<string, object?>>
                {
                    new KeyValuePair<string, object?>("api_environment", _environment.EnvironmentName),
                    new KeyValuePair<string, object?>("request_id", requestUuid),
                    new KeyValuePair<string, object?>("trace_id", traceId),
                    new KeyValuePair<string, object?>("span_id", spanId),
                    new KeyValuePair<string, object?>("endpoint_handler", context.GetEndpoint()?.DisplayName ?? string.Empty),
                    new KeyValuePair<string, object?>("method", method),
                    new KeyValuePair<string, object?>("pathname", pathname),
                    new KeyValuePair<string, object?>("host", host),
                    new KeyValuePair<string, object?>("remote_addr", remoteAddr),
                    new KeyValuePair<string, object?>("server_hostname", Environment.MachineName),
                    new KeyValuePair<string, object?>("status_code", statusCode),
                    new KeyValuePair<string, object?>("latency_ms", latencyMs),
                    new KeyValuePair<string, object?>("req_body_bytes", reqBodyBytes),
                    new KeyValuePair<string, object?>("res_body_bytes", resBodyBytes)
                };

                if (caught != null)
                {
                    state.Add(new KeyValuePair<string, object?>("error_type", caught.GetType().Name));
                    state.Add(new KeyValuePair<string, object?>("error_location", ResolveErrorLocation(caught)));
                }

                if (captureBodies)
                {
                    state.Add(new KeyValuePair<string, object?>("request_body", Truncate(Logging.PiiMasker.Mask(requestBody), MaxBodyLength)));
                    state.Add(new KeyValuePair<string, object?>("response_body", Truncate(Logging.PiiMasker.Mask(responseBody), MaxBodyLength)));
                }

                var message = FormatMessage(method, pathname, statusCode, latencyMs);
                _logger.Log(level, default, state, caught, (_, _) => message);
            }
        }

        private static string FormatMessage(string method, string pathname, int statusCode, double latencyMs)
        {
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "HTTP {0} {1} -> {2} ({3:0.0}ms)",
                method, pathname, statusCode, latencyMs);
        }

        private static LogLevel DetermineLevel(int statusCode, Exception? exception)
        {
            if (exception != null) return LogLevel.Error;
            if (statusCode >= 500) return LogLevel.Error;
            if (statusCode >= 400) return LogLevel.Warning;
            return LogLevel.Information;
        }

        private static string ResolveRequestUuid(HttpContext context)
        {
            foreach (var name in RequestIdHeaderCandidates)
            {
                if (context.Request.Headers.TryGetValue(name, out var values))
                {
                    var candidate = values.ToString();
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                }
            }
            return Guid.CreateVersion7().ToString("N");
        }

        private static string ResolveTraceId(HttpContext context)
        {
            var activityTraceId = Activity.Current?.TraceId.ToString();
            if (!string.IsNullOrEmpty(activityTraceId) && activityTraceId != "00000000000000000000000000000000")
            {
                return activityTraceId!;
            }
            return context.TraceIdentifier;
        }

        private static string? ResolveErrorLocation(Exception exception)
        {
            var trace = new StackTrace(exception, fNeedFileInfo: true);
            for (var i = 0; i < trace.FrameCount; i++)
            {
                var frame = trace.GetFrame(i);
                var file = frame?.GetFileName();
                if (!string.IsNullOrEmpty(file))
                {
                    return Path.GetFileName(file) + ":" + frame!.GetFileLineNumber();
                }
            }

            var method = trace.GetFrame(0)?.GetMethod();
            return method == null ? null : method.DeclaringType?.Name + "." + method.Name;
        }

        private static async Task<string?> ReadRequestBodyAsync(HttpRequest request)
        {
            if (!HasReadableBody(request)) return null;

            request.EnableBuffering();
            request.Body.Position = 0;
            using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
            {
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;
                // Returned untruncated: PII masking runs on the full body, then the caller
                // truncates the masked result (see InvokeAsync) so a cut never leaks PII.
                return body;
            }
        }

        private static bool HasReadableBody(HttpRequest request)
        {
            if (request.ContentLength.HasValue && request.ContentLength.Value == 0) return false;
            var method = request.Method;
            return HttpMethods.IsPost(method)
                || HttpMethods.IsPut(method)
                || HttpMethods.IsPatch(method)
                || HttpMethods.IsDelete(method);
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (value == null) return null;
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength) + "...[truncated]";
        }
    }
}
