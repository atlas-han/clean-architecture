using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Api.Middleware
{
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

        public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            var requestId = ResolveRequestId(context);
            var traceId = ResolveTraceId(context);

            context.Response.Headers[RequestIdHeader] = requestId;

            var path = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;
            var queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value! : string.Empty;
            var method = context.Request.Method;

            var requestBody = await ReadRequestBodyAsync(context.Request);

            var captureResponse = _logger.IsEnabled(LogLevel.Debug);
            var originalBodyStream = context.Response.Body;
            MemoryStream? responseBuffer = null;
            if (captureResponse)
            {
                responseBuffer = new MemoryStream();
                context.Response.Body = responseBuffer;
            }

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
                if (captureResponse && responseBuffer != null)
                {
                    try
                    {
                        responseBuffer.Position = 0;
                        using (var reader = new StreamReader(responseBuffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                        {
                            responseBody = Truncate(await reader.ReadToEndAsync(), MaxBodyLength);
                        }
                        responseBuffer.Position = 0;
                        await responseBuffer.CopyToAsync(originalBodyStream);
                    }
                    finally
                    {
                        context.Response.Body = originalBodyStream;
                        responseBuffer.Dispose();
                    }
                }

                var statusCode = context.Response.StatusCode;
                var level = DetermineLevel(statusCode, caught);

                var state = new List<KeyValuePair<string, object?>>
                {
                    new KeyValuePair<string, object?>("Timestamp", DateTimeOffset.UtcNow.ToString("o")),
                    new KeyValuePair<string, object?>("TraceId", traceId),
                    new KeyValuePair<string, object?>("RequestId", requestId),
                    new KeyValuePair<string, object?>("Method", method),
                    new KeyValuePair<string, object?>("Path", path),
                    new KeyValuePair<string, object?>("QueryString", queryString),
                    new KeyValuePair<string, object?>("StatusCode", statusCode),
                    new KeyValuePair<string, object?>("ProcessingTimeMs", stopwatch.Elapsed.TotalMilliseconds),
                    new KeyValuePair<string, object?>("RequestBody", requestBody)
                };

                if (captureResponse)
                {
                    state.Add(new KeyValuePair<string, object?>("ResponseBody", responseBody));
                }

                _logger.Log(level, default, state, caught, FormatRequest);
            }
        }

        private static string FormatRequest(IReadOnlyList<KeyValuePair<string, object?>> state, Exception? exception)
        {
            return "request_handled";
        }

        private static LogLevel DetermineLevel(int statusCode, Exception? exception)
        {
            if (exception != null) return LogLevel.Error;
            if (statusCode >= 500) return LogLevel.Error;
            if (statusCode >= 400) return LogLevel.Warning;
            return LogLevel.Information;
        }

        private static string ResolveRequestId(HttpContext context)
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
            return Guid.NewGuid().ToString("N");
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

        private static async Task<string?> ReadRequestBodyAsync(HttpRequest request)
        {
            if (!HasReadableBody(request)) return null;

            request.EnableBuffering();
            request.Body.Position = 0;
            using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
            {
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0;
                return Truncate(body, MaxBodyLength);
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
