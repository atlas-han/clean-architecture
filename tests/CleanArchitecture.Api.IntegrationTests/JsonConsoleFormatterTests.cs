using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CleanArchitecture.Api.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace CleanArchitecture.Api.IntegrationTests
{
    public class JsonConsoleFormatterTests
    {
        [Fact]
        public void Write_EmitsSnakeCaseJson_WithBaseFields()
        {
            var formatter = new JsonConsoleFormatter();
            var state = new List<KeyValuePair<string, object?>>
            {
                new KeyValuePair<string, object?>("RequestId", "req-1"),
                new KeyValuePair<string, object?>("TraceId", "trace-1"),
                new KeyValuePair<string, object?>("ProcessingTimeMs", 12.5d),
                new KeyValuePair<string, object?>("QueryString", "?x=1"),
                new KeyValuePair<string, object?>("StatusCode", 200)
            };

            var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
                LogLevel.Information,
                "Some.Category",
                eventId: default,
                state: state,
                exception: null,
                formatter: (_, _) => "request_handled");

            var writer = new StringWriter();
            formatter.Write(in entry, null, writer);

            var output = writer.ToString().Trim();
            Assert.False(string.IsNullOrEmpty(output));

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            Assert.Equal("info", root.GetProperty("level").GetString());
            Assert.Equal("Some.Category", root.GetProperty("category").GetString());
            Assert.Equal("request_handled", root.GetProperty("message").GetString());
            Assert.True(root.TryGetProperty("timestamp", out _));

            Assert.Equal("req-1", root.GetProperty("request_id").GetString());
            Assert.Equal("trace-1", root.GetProperty("trace_id").GetString());
            Assert.Equal(12.5d, root.GetProperty("processing_time_ms").GetDouble());
            Assert.Equal("?x=1", root.GetProperty("query_string").GetString());
            Assert.Equal(200, root.GetProperty("status_code").GetInt32());

            Assert.False(root.TryGetProperty("RequestId", out _));
            Assert.False(root.TryGetProperty("ProcessingTimeMs", out _));
        }

        [Fact]
        public void Write_MapsLogLevelsCorrectly()
        {
            Assert.Equal("debug", RenderLevel(LogLevel.Debug));
            Assert.Equal("info", RenderLevel(LogLevel.Information));
            Assert.Equal("warning", RenderLevel(LogLevel.Warning));
            Assert.Equal("error", RenderLevel(LogLevel.Error));
        }

        [Fact]
        public void ToSnakeCase_HandlesCamelAndPascal()
        {
            Assert.Equal("request_id", JsonConsoleFormatter.ToSnakeCase("RequestId"));
            Assert.Equal("processing_time_ms", JsonConsoleFormatter.ToSnakeCase("ProcessingTimeMs"));
            Assert.Equal("trace_id", JsonConsoleFormatter.ToSnakeCase("traceId"));
            Assert.Equal("path", JsonConsoleFormatter.ToSnakeCase("Path"));
        }

        private static string RenderLevel(LogLevel level)
        {
            var formatter = new JsonConsoleFormatter();
            var state = new List<KeyValuePair<string, object?>>();
            var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
                level, "Cat", default, state, null, (_, _) => "msg");
            var writer = new StringWriter();
            formatter.Write(in entry, null, writer);
            using var doc = JsonDocument.Parse(writer.ToString().Trim());
            return doc.RootElement.GetProperty("level").GetString()!;
        }
    }
}
