using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
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
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Password=none")
            .Options;

    private readonly MovableDateTimeProvider _clock = new();

    public static TheoryData<SecurityEventKind> EveryKind =>
        [.. Enum.GetValues<SecurityEventKind>()];

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Record_HandlesEveryDeclaredKindWithoutThrowing(SecurityEventKind kind) =>
        Should.NotThrow(() => Log().Record(new SecurityEvent(kind, Guid.CreateVersion7())));

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
