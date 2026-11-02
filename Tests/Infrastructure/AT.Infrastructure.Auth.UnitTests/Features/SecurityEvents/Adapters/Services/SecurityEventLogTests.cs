using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.SecurityEvents.Adapters.Services;

public sealed class SecurityEventLogTests
{
    private static readonly DbContextOptions<AuthDbContext> _unreachableOptions =
        new DbContextOptionsBuilder<AuthDbContext>()
            .UseDatabase(DatabaseFaults.UnreachableConnectionString)
            .Options;

    private readonly MovableDateTimeProvider _clock = new();

    [Fact]
    public void Record_HandlesEveryDeclaredKind()
    {
        var log = Log();

        SecurityEventKind[] offenders =
        [
            .. Enum.GetValues<SecurityEventKind>().Where(kind => !Records(log, kind)),
        ];

        offenders.ShouldBeEmpty("A kind the log cannot write is an event the account's history loses.");
    }

    [Fact]
    public void Record_StillWritesTheStructuredEntry_WhenTheRowCannotBeStored()
    {
        var logger = new RecordingLogger();
        var userId = Guid.CreateVersion7();

        LogFor(logger).Record(SecurityEvent.LoginSucceeded(userId));

        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information && entry.Message.Contains("Login succeeded", StringComparison.Ordinal));
        logger.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains(nameof(SecurityEventKind.LoginSucceeded), StringComparison.Ordinal)
            && entry.Exception != null);
    }

    private static bool Records(SecurityEventLog log, SecurityEventKind kind)
    {
        try
        {
            log.Record(new SecurityEvent(kind, Guid.CreateVersion7()));

            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private SecurityEventLog LogFor(ILogger<SecurityEventLog> logger) =>
        new(
            logger,
            _unreachableOptions,
            new AuthDbContext(_unreachableOptions),
            new UnknownSecurityEventOriginProvider(),
            _clock,
            Microsoft.Extensions.Options.Options.Create(new SecurityEventLogOptions()));

    private SecurityEventLog Log() => LogFor(NullLogger<SecurityEventLog>.Instance);

    private sealed class RecordingLogger : ILogger<SecurityEventLog>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
