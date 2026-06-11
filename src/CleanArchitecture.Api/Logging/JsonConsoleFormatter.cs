using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace CleanArchitecture.Api.Logging
{
    // Emits the unified API server logging spec (API design guide §14.3).
    // Field names and casing are a fixed contract with the log pipeline (ELK/CloudWatch):
    // keys are written verbatim — do NOT re-case them here.
    public class JsonConsoleFormatter : ConsoleFormatter
    {
        public const string FormatterName = "unified_json";

        private const string OriginalFormatKey = "{OriginalFormat}";

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
                    ["stackTrace"] = logEntry.Exception.StackTrace
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
                    if (kvp.Key == "RequestPath") continue;
                    target[kvp.Key] = kvp.Value;
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
                    target[kvp.Key] = kvp.Value;
                }
            }
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
