using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common;

internal sealed class RecordingLogger<TCategoryName> : ILogger<TCategoryName>
{
    internal List<LoggedEntry> Entries { get; } = [];

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
        ArgumentNullException.ThrowIfNull(formatter);

        Entries.Add(new LoggedEntry(logLevel, formatter(state, exception), exception));
    }
}
