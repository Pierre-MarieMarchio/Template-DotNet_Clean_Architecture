using Microsoft.Extensions.Logging;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <param name="Category">The logger category, i.e. the type that wrote it.</param>
public sealed record LogRecord(string Category, LogLevel Level, string Message);

/// <summary>
/// The log the host writes to during a test. It exists so a test can assert that a piece of
/// behaviour whose only observable effect <em>is</em> a log entry actually ran — the product's
/// domain-event consumer is exactly that, and asserting on a recording double registered alongside
/// it would only prove the double ran.
/// </summary>
public sealed class CapturedLogs
{
    /// <summary>A ceiling, so a test that somehow provokes a logging loop fails on an assertion
    /// rather than on memory.</summary>
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
        // Nothing to release: the sink outlives the provider and is owned by the container.
    }

    private sealed class CapturingLogger(string category, CapturedLogs captured) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <summary>
        /// Always true. Whether a category is enabled is decided by the filter in front of this
        /// logger, from configuration; answering false here would hide records the host considers
        /// enabled and make an assertion on them meaningless.
        /// </summary>
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
