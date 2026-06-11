using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Api.IntegrationTests.Infrastructure
{
    public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new ConcurrentQueue<CapturedLogEntry>();
        private readonly LogLevel _minLevel;
        private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

        public CapturingLoggerProvider(LogLevel minLevel)
        {
            _minLevel = minLevel;
        }

        public IReadOnlyList<CapturedLogEntry> Entries => _entries.ToArray();

        public void Clear()
        {
            while (_entries.TryDequeue(out _))
            {
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _minLevel, _entries, () => _scopeProvider);

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopeProvider = scopeProvider;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly string _category;
            private readonly LogLevel _minLevel;
            private readonly ConcurrentQueue<CapturedLogEntry> _sink;
            private readonly Func<IExternalScopeProvider> _scopeProvider;

            public CapturingLogger(string category, LogLevel minLevel, ConcurrentQueue<CapturedLogEntry> sink, Func<IExternalScopeProvider> scopeProvider)
            {
                _category = category;
                _minLevel = minLevel;
                _sink = sink;
                _scopeProvider = scopeProvider;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => _scopeProvider().Push(state);

            public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;

                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                _scopeProvider().ForEachScope((scope, target) => MergeValues(scope, target), values);
                MergeValues(state, values);

                _sink.Enqueue(new CapturedLogEntry(_category, logLevel, formatter(state, exception), values, exception));
            }

            private static void MergeValues(object? source, Dictionary<string, object?> target)
            {
                if (source is IEnumerable<KeyValuePair<string, object?>> kvps)
                {
                    foreach (var kvp in kvps)
                    {
                        if (kvp.Key == "{OriginalFormat}") continue;
                        target[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
    }

    public sealed class CapturedLogEntry
    {
        public CapturedLogEntry(string category, LogLevel level, string message, IReadOnlyDictionary<string, object?> values, Exception? exception)
        {
            Category = category;
            Level = level;
            Message = message;
            Values = values;
            Exception = exception;
        }

        public string Category { get; }
        public LogLevel Level { get; }
        public string Message { get; }
        public IReadOnlyDictionary<string, object?> Values { get; }
        public Exception? Exception { get; }
    }
}
