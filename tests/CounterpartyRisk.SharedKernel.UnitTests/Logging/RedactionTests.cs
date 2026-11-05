using CounterpartyRisk.ServiceDefaults;
using CounterpartyRisk.SharedKernel.Compliance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CounterpartyRisk.SharedKernel.UnitTests.Logging;

/// <summary>
/// End-to-end check of the redaction chain: source-generated log method, production logging
/// setup from ServiceDefaults, then a sink standing for the console and OTLP exporters.
/// The architecture tests check that production assemblies use the same generator.
/// </summary>
public partial class RedactionTests
{
    private const string Lei = "969500T3M8S4ZGR8AB12";
    private const string UserEmail = "jane.doe@example.org";

    [Fact]
    public void Classified_parameters_never_reach_the_log_sinks()
    {
        using var sink = new CapturingLoggerProvider();
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddLogRedaction();
        builder.Logging.AddProvider(sink);
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILogger<RedactionTests>>();

        LogExposureRead(logger, Lei, UserEmail, "visible-status");

        var entry = sink.Entries.ShouldHaveSingleItem();
        entry.ShouldContain("visible-status");
        entry.ShouldNotContain(Lei);
        entry.ShouldNotContain(UserEmail);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Exposure read for {Lei} by {User}: {Status}")]
    private static partial void LogExposureRead(
        ILogger logger,
        [SensitiveData] string lei,
        [PersonalData] string user,
        string status);

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public List<string> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            // Both the formatted message and the structured state reach real exporters.
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            var structured = string.Join('|', properties.Select(p => $"{p.Key}={p.Value}"));
            Entries.Add($"{formatter(state, exception)} || {structured}");
        }

        public void Dispose()
        {
        }
    }
}
