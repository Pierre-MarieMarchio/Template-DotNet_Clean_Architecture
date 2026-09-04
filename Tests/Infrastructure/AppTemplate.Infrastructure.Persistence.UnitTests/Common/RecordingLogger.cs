using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that keeps what it was told, so a test can assert that a
/// swallowed failure was at least reported.
/// </summary>
/// <remarks>
/// Hand-written rather than substituted: <c>ILogger.Log</c> takes the state as a generic parameter and
/// formats it through a delegate, so a recorded call is unreadable until the delegate has been applied.
/// Applying it here is both shorter and clearer than matching on the argument.
/// </remarks>
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
