using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.UseCases.Commands.LockAccount;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.LockAccount;

public sealed class LockAccountUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IAccountLockouts _lockouts = Substitute.For<IAccountLockouts>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _lockouts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new LockAccountCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _lockouts.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Locking rotates the security stamp, which would invalidate the very access token the caller
    /// used to make this request — an administrator could otherwise lock themselves out with no
    /// other session left to undo it.
    /// </summary>
    [Fact]
    public async Task ACallerTargetingThemselves_IsRefusedBeforeTouchingTheStore()
    {
        var result = await UseCaseFor(StubCurrentUser.WithId(_callerId))
            .ExecuteAsync(new LockAccountCommand(_callerId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.lockout.cannotTargetSelf");
        _lockouts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.Applied);

        var result = await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>The gap this whole vertical exists to close: see <c>AccountLockouts</c>.</summary>
    [Fact]
    public async Task ASuccessfulLock_RevokesEveryRefreshTokenForTheTarget()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.Applied);

        await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(_targetId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulLock_RecordsBothTheAdministrativeActionAndTheStampRotation()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.Applied);

        await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.AccountLockedByAdministrator(_targetId));
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_targetId));
    }

    [Fact]
    public async Task AStoreRefusal_RevokesNothing()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.Rejected);

        var result = await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private void GivenTheOutcomeIs(LockoutChangeOutcome outcome) =>
        _lockouts.LockAsync(_targetId, Arg.Any<CancellationToken>()).Returns(outcome);

    private LockAccountUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_lockouts, _refreshTokens, _securityEventLog, currentUser, new LockAccountCommandValidator());

    private LockAccountUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
