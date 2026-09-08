using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Worker.UnitTests.TestSupport;

/// <summary>
/// Keeps every formatted line a service logged, so a test can assert on one. Concurrent because a
/// host with several loops logs from several threads.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _lines = new();

    internal IReadOnlyCollection<(LogLevel Level, string Message)> Lines => _lines;

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

        _lines.Enqueue((logLevel, formatter(state, exception)));
    }
}
