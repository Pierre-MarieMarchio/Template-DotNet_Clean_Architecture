using Microsoft.Extensions.Logging;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed record LogRecord(string Category, LogLevel Level, string Message);

public sealed class CapturedLogs
{
    private const int _maxRecords = 20_000;

    private readonly object _gate = new();
    private readonly List<LogRecord> _records = [];

    public void Record(LogRecord record)
    {
        lock (_gate)
        {
            if (_records.Count < _maxRecords)
            {
                _records.Add(record);
            }
        }
    }

    public IReadOnlyList<LogRecord> Snapshot()
    {
        lock (_gate)
        {
            return [.. _records];
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _records.Clear();
        }
    }
}

internal sealed class CapturingLoggerProvider(CapturedLogs captured) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, captured);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, CapturedLogs captured) : ILogger
    {
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

            captured.Record(new LogRecord(category, logLevel, formatter(state, exception)));
        }
    }
}
