using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace CleanArchitecture.Api.Logging
{
    public class JsonConsoleFormatter : ConsoleFormatter
    {
        public const string FormatterName = "snake_json";

        private const string OriginalFormatKey = "{OriginalFormat}";

        public JsonConsoleFormatter() : base(FormatterName)
        {
        }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);

            payload["timestamp"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            payload["level"] = MapLevel(logEntry.LogLevel);
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
                payload["exception"] = logEntry.Exception.ToString();
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
                    target[ToSnakeCase(kvp.Key)] = kvp.Value;
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
                    target[ToSnakeCase(kvp.Key)] = kvp.Value;
                }
            }
        }

        private static string MapLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => "trace",
                LogLevel.Debug => "debug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warning",
                LogLevel.Error => "error",
                LogLevel.Critical => "critical",
                _ => "none"
            };
        }

        public static string ToSnakeCase(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.IndexOf('_') >= 0 && !ContainsUpper(value)) return value.ToLowerInvariant();

            var builder = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsUpper(c))
                {
                    if (i > 0 && value[i - 1] != '_' && (char.IsLower(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
                    {
                        builder.Append('_');
                    }
                    builder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    builder.Append(c);
                }
            }
            return builder.ToString();
        }

        private static bool ContainsUpper(string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                if (char.IsUpper(value[i])) return true;
            }
            return false;
        }
    }
}
