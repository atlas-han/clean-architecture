using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CleanArchitecture.Api.Logging;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class JsonConsoleFormatterTests
    {
        private static JsonConsoleFormatter CreateFormatter()
        {
            return new JsonConsoleFormatter(new FakeHostEnvironment { ApplicationName = "test-api" });
        }

        [Fact]
        public void Write_EmitsContractFieldNamesVerbatim()
        {
            var formatter = CreateFormatter();
            var state = new List<KeyValuePair<string, object?>>
            {
                new KeyValuePair<string, object?>("requestUUID", "req-1"),
                new KeyValuePair<string, object?>("traceID", "trace-1"),
                new KeyValuePair<string, object?>("latencyMs", 12.5d),
                new KeyValuePair<string, object?>("pathname", "/health?x=1"),
                new KeyValuePair<string, object?>("statusCode", 200),
                new KeyValuePair<string, object?>("error_type", "ValidationError")
            };

            var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
                LogLevel.Information,
                "Some.Category",
                eventId: default,
                state: state,
                exception: null,
                formatter: (_, _) => "HTTP GET /health?x=1 -> 200 (12.5ms)");

            var writer = new StringWriter();
            formatter.Write(in entry, null, writer);

            var output = writer.ToString().Trim();
            Assert.False(string.IsNullOrEmpty(output));

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            Assert.Equal("INFO", root.GetProperty("level").GetString());
            Assert.Equal("test-api", root.GetProperty("service").GetString());
            Assert.Equal("Some.Category", root.GetProperty("category").GetString());
            Assert.Equal("HTTP GET /health?x=1 -> 200 (12.5ms)", root.GetProperty("message").GetString());
            Assert.True(root.TryGetProperty("timestamp", out _));

            // Keys pass through verbatim — the §14.3 spec casing is the pipeline contract.
            Assert.Equal("req-1", root.GetProperty("requestUUID").GetString());
            Assert.Equal("trace-1", root.GetProperty("traceID").GetString());
            Assert.Equal(12.5d, root.GetProperty("latencyMs").GetDouble());
            Assert.Equal("/health?x=1", root.GetProperty("pathname").GetString());
            Assert.Equal(200, root.GetProperty("statusCode").GetInt32());
            Assert.Equal("ValidationError", root.GetProperty("error_type").GetString());

            Assert.False(root.TryGetProperty("request_uuid", out _));
            Assert.False(root.TryGetProperty("trace_id", out _));
            Assert.False(root.TryGetProperty("latency_ms", out _));
        }

        [Fact]
        public void Write_MergesScopeKeysVerbatim()
        {
            var formatter = CreateFormatter();
            var scopeProvider = new LoggerExternalScopeProvider();
            using var scope = scopeProvider.Push(new Dictionary<string, object?>
            {
                ["traceID"] = "trace-9",
                ["spanID"] = "span-9",
                ["requestUUID"] = "req-9"
            });
            // Hosting/Kestrel scope noise — duplicates the contract fields and must be dropped.
            using var hostingScope = scopeProvider.Push(new Dictionary<string, object?>
            {
                ["RequestId"] = "0HNM:00000001",
                ["RequestPath"] = "/health",
                ["TraceId"] = "trace-9",
                ["SpanId"] = "span-9",
                ["ParentId"] = "0000000000000000",
                ["ConnectionId"] = "0HNM"
            });

            var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
                LogLevel.Information,
                "Some.Category",
                eventId: default,
                state: new List<KeyValuePair<string, object?>>(),
                exception: null,
                formatter: (_, _) => "in-request log");

            var writer = new StringWriter();
            formatter.Write(in entry, scopeProvider, writer);

            using var doc = JsonDocument.Parse(writer.ToString().Trim());
            var root = doc.RootElement;

            Assert.Equal("trace-9", root.GetProperty("traceID").GetString());
            Assert.Equal("span-9", root.GetProperty("spanID").GetString());
            Assert.Equal("req-9", root.GetProperty("requestUUID").GetString());

            Assert.False(root.TryGetProperty("RequestId", out _));
            Assert.False(root.TryGetProperty("RequestPath", out _));
            Assert.False(root.TryGetProperty("TraceId", out _));
            Assert.False(root.TryGetProperty("SpanId", out _));
            Assert.False(root.TryGetProperty("ParentId", out _));
            Assert.False(root.TryGetProperty("ConnectionId", out _));
        }

        [Fact]
        public void Write_MapsLogLevelsToSpecNames()
        {
            Assert.Equal("TRACE", RenderLevel(LogLevel.Trace));
            Assert.Equal("DEBUG", RenderLevel(LogLevel.Debug));
            Assert.Equal("INFO", RenderLevel(LogLevel.Information));
            Assert.Equal("WARNING", RenderLevel(LogLevel.Warning));
            Assert.Equal("ERROR", RenderLevel(LogLevel.Error));
            Assert.Equal("CRITICAL", RenderLevel(LogLevel.Critical));
        }

        [Fact]
        public void Write_SerializesExceptionAsStructuredObject()
        {
            var formatter = CreateFormatter();
            Exception thrown;
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
                LogLevel.Error,
                "Some.Category",
                eventId: default,
                state: new List<KeyValuePair<string, object?>>(),
                exception: thrown,
                formatter: (_, _) => "failed");

            var writer = new StringWriter();
            formatter.Write(in entry, null, writer);

            using var doc = JsonDocument.Parse(writer.ToString().Trim());
            var exception = doc.RootElement.GetProperty("exception");

            Assert.Equal(JsonValueKind.Object, exception.ValueKind);
            Assert.Equal("System.InvalidOperationException", exception.GetProperty("type").GetString());
            Assert.Equal("boom", exception.GetProperty("message").GetString());
            Assert.False(string.IsNullOrEmpty(exception.GetProperty("stackTrace").GetString()));
        }

        [Fact]
        public void Write_CoercesNonSerializableStateValue_DoesNotThrow()
        {
            var formatter = CreateFormatter();
            // EF Core's QueryIterationFailed diagnostic event carries the DbContext
            // type (a System.Type / RuntimeType) in its log state. System.Text.Json
            // cannot serialize Type, so before hardening this made the logger throw
            // an AggregateException that masked the real DB error and 500'd /api/Orders.
            var state = new List<KeyValuePair<string, object?>>
            {
                new KeyValuePair<string, object?>("contextType", typeof(JsonConsoleFormatter))
            };

            var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
                LogLevel.Error,
                "Microsoft.EntityFrameworkCore.Query",
                eventId: default,
                state: state,
                exception: null,
                formatter: (_, _) => "query iteration failed");

            var writer = new StringWriter();

            var thrown = Record.Exception(() => formatter.Write(in entry, null, writer));
            Assert.Null(thrown);

            using var doc = JsonDocument.Parse(writer.ToString().Trim());
            Assert.Equal(
                typeof(JsonConsoleFormatter).ToString(),
                doc.RootElement.GetProperty("contextType").GetString());
        }

        private static string RenderLevel(LogLevel level)
        {
            var formatter = CreateFormatter();
            var state = new List<KeyValuePair<string, object?>>();
            var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
                level, "Cat", default, state, null, (_, _) => "msg");
            var writer = new StringWriter();
            formatter.Write(in entry, null, writer);
            using var doc = JsonDocument.Parse(writer.ToString().Trim());
            return doc.RootElement.GetProperty("level").GetString()!;
        }

        private sealed class FakeHostEnvironment : IHostEnvironment
        {
            public string ApplicationName { get; set; } = "test-api";
            public string EnvironmentName { get; set; } = "Development";
            public string ContentRootPath { get; set; } = ".";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
