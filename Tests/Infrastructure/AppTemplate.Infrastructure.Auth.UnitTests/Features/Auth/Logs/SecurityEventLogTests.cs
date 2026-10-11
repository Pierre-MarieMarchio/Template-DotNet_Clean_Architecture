using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Auth.Logs;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using AppTemplate.Infrastructure.Auth.Features.Auth.Providers;
using AppTemplate.Infrastructure.Auth.UnitTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Auth.Logs;

/// <summary>
/// <see cref="SecurityEventLog"/> dispatches on <see cref="SecurityEventKind"/> with a switch that
/// falls back to <see cref="ArgumentOutOfRangeException"/> — deliberately, so a kind nobody wired a
/// log line for fails loudly instead of vanishing silently. That default arm has to stay reachable
/// only in theory: every kind the enum actually declares must have a case above it, or every
/// operation that records one throws a 500 the caller never asked for.
/// <para>
/// The same tests now carry the other guarantee this adapter makes: the row is written on the
/// adapter's own, and a database that cannot take it is logged, not propagated — no event kind, and
/// no action that recorded one, is failed by the trail's own storage. The database here is
/// unreachable on purpose; the connection attempt is refused, not timed out.
/// </para>
/// </summary>
public sealed class SecurityEventLogTests
{
    /// <summary>A port nothing listens on, so the row write fails fast and deterministically.</summary>
    private static readonly DbContextOptions<AuthDbContext> _unreachableOptions =
        new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unreachable;Username=none;Password=none")
            .Options;

    private readonly MovableDateTimeProvider _clock = new();

    public static TheoryData<SecurityEventKind> EveryKind =>
        [.. Enum.GetValues<SecurityEventKind>()];

    /// <summary>
    /// Read off the enum rather than listed, so a kind added without a case here is caught by this
    /// test instead of by the first caller to reach it in production — the exact gap that let
    /// <c>TwoFactorEnabled</c>/<c>TwoFactorDisabled</c>/<c>TwoFactorChallengeFailed</c>/
    /// <c>RecoveryCodeRedeemed</c> ship as a 500 on <c>/auth/two-factor/confirm</c> the first time
    /// anything actually called <see cref="ISecurityEventLog.Record"/> with one of them.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Record_HandlesEveryDeclaredKindWithoutThrowing(SecurityEventKind kind) =>
        Should.NotThrow(() => Log().Record(new SecurityEvent(kind, Guid.CreateVersion7())));

    /// <summary>
    /// The row write is the trail's own concern: when the table cannot take the event, the
    /// structured entry is still emitted, the failure is logged at <c>Error</c> naming the kind,
    /// and the caller — the action that decided the fact — is left alone.
    /// </summary>
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

    /// <summary>Formats the structured state the source generator hands it, so the template is asserted.</summary>
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
