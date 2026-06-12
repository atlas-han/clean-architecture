using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace CleanArchitecture.Infrastructure.Logging
{
    public static class LoggingExtensions
    {
        // Installs the unified JSON console log format (§14.3) as the single console provider.
        // Every host (the Api and the outbox Worker) calls this so both processes emit identical
        // structured log lines to the same pipeline. Clearing the default providers first drops
        // the framework's plain-text console formatter, leaving JsonConsoleFormatter as the only
        // one — the FormatterName below selects it in code, so no Logging:Console:FormatterName
        // entry is required in either host's appsettings (the ones some already carry are redundant
        // but harmless). The formatter is resolved from DI (it depends on IHostEnvironment for the
        // service name), so AddConsoleFormatter registers the type.
        public static ILoggingBuilder AddUnifiedConsoleLogging(this ILoggingBuilder builder)
        {
            builder.ClearProviders();
            builder.AddConsole(options => options.FormatterName = JsonConsoleFormatter.FormatterName);
            builder.AddConsoleFormatter<JsonConsoleFormatter, ConsoleFormatterOptions>();
            return builder;
        }
    }
}
