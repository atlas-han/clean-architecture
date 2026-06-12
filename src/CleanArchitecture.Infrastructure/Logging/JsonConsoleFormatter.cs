using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace CleanArchitecture.Infrastructure.Logging
{
    // Emits the unified server logging spec (API design guide §14.3). Shared by every host
    // process (the Api and the outbox Worker) via AddUnifiedConsoleLogging, so both emit
    // identical structured log lines to the same pipeline (ELK/CloudWatch).
    // Field names and casing are a fixed contract with that pipeline:
    // state/scope keys are written verbatim (they arrive already in snake_case from the
    // source) — do NOT re-case them here. The keys this class owns are snake_case too.
    public class JsonConsoleFormatter : ConsoleFormatter
    {
        public const string FormatterName = "unified_json";

        private const string OriginalFormatKey = "{OriginalFormat}";

        // ASP.NET hosting/Kestrel scopes inject these PascalCase keys on every request log;
        // they duplicate the §14.3 contract fields (trace_id/span_id/request_id) as noise.
        private static readonly HashSet<string> ExcludedScopeKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "RequestPath",
            "RequestId",
            "TraceId",
            "SpanId",
            "ParentId",
            "ConnectionId"
        };

        private readonly string _serviceName;

        public JsonConsoleFormatter(IHostEnvironment environment) : base(FormatterName)
        {
            _serviceName = environment.ApplicationName;
        }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);

            payload["timestamp"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            payload["level"] = MapLevel(logEntry.LogLevel);
            payload["service"] = _serviceName;
            payload["category"] = logEntry.Category;

            if (scopeProvider != null)
            {
                scopeProvider.ForEachScope((scope, target) => MergeScope(scope, target), payload);
            }

            MergeState(logEntry.State, payload);

            var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
            if (!string.IsNullOrEmpty(message))
            {
                payload["message"] = message;
            }

            if (logEntry.Exception != null)
            {
                payload["exception"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = logEntry.Exception.GetType().FullName,
                    ["message"] = logEntry.Exception.Message,
                    ["stack_trace"] = logEntry.Exception.StackTrace
                };
            }

            var json = JsonSerializer.Serialize(payload);
            textWriter.WriteLine(json);
        }

        private static void MergeScope(object? scope, Dictionary<string, object?> target)
        {
            if (scope is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                foreach (var kvp in kvps)
                {
                    if (kvp.Key == OriginalFormatKey) continue;
                    if (ExcludedScopeKeys.Contains(kvp.Key)) continue;
                    target[kvp.Key] = ToJsonSafe(kvp.Value);
                }
            }
        }

        private static void MergeState(object? state, Dictionary<string, object?> target)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                foreach (var kvp in kvps)
                {
                    if (kvp.Key == OriginalFormatKey) continue;
                    target[kvp.Key] = ToJsonSafe(kvp.Value);
                }
            }
        }

        // Log state/scope can carry arbitrary values. Some — notably System.Type
        // (RuntimeType), which EF Core puts in diagnostic events such as
        // QueryIterationFailed — are not JSON-serializable, and would make
        // JsonSerializer.Serialize throw. ConsoleFormatter surfaces that as an
        // AggregateException that masks the original error and 500s the request.
        // A logger must never throw, so coerce anything that isn't a JSON-friendly
        // scalar to its string form.
        private static object? ToJsonSafe(object? value)
        {
            return value switch
            {
                null => null,
                string or bool or char
                    or byte or sbyte or short or ushort or int or uint or long or ulong
                    or float or double or decimal
                    or DateTime or DateTimeOffset or TimeSpan or Guid or Enum => value,
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
            };
        }

        private static string MapLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARNING",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRITICAL",
                _ => "NONE"
            };
        }
    }
}
