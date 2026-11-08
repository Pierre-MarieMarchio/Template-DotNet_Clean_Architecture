using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;

internal sealed class ConcurrentRecordingLogger<T> : ILogger<T>
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
