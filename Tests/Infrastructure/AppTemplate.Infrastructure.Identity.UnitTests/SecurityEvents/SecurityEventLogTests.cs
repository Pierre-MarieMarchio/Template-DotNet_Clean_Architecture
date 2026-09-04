using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Infrastructure.Identity.SecurityEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.SecurityEvents;

/// <summary>
/// <see cref="SecurityEventLog"/> dispatches on <see cref="SecurityEventKind"/> with a switch that
/// falls back to <see cref="ArgumentOutOfRangeException"/> — deliberately, so a kind nobody wired a
/// log line for fails loudly instead of vanishing silently. That default arm has to stay reachable
/// only in theory: every kind the enum actually declares must have a case above it, or every
/// operation that records one throws a 500 the caller never asked for.
/// </summary>
public sealed class SecurityEventLogTests
{
    private static readonly SecurityEventLog _log = new(NullLogger<SecurityEventLog>.Instance);

    /// <summary>
    /// Read off the enum rather than listed, so a kind added without a case here is caught by this
    /// test instead of by the first caller to reach it in production — the exact gap that let
    /// <c>TwoFactorEnabled</c>/<c>TwoFactorDisabled</c>/<c>TwoFactorChallengeFailed</c>/
    /// <c>RecoveryCodeRedeemed</c> ship as a 500 on <c>/auth/two-factor/confirm</c> the first time
    /// anything actually called <see cref="ISecurityEventLog.Record"/> with one of them.
    /// </summary>
    public static TheoryData<SecurityEventKind> EveryKind =>
        [.. Enum.GetValues<SecurityEventKind>()];

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void Record_HandlesEveryDeclaredKindWithoutThrowing(SecurityEventKind kind) =>
        Should.NotThrow(() => _log.Record(new SecurityEvent(kind, Guid.CreateVersion7())));
}
