using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.UseCases.Commands.UnlockAccount;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.UnlockAccount;

public sealed class UnlockAccountUseCaseTests
{
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IAccountLockouts _lockouts = Substitute.For<IAccountLockouts>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new UnlockAccountCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _lockouts.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(new UnlockAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.Applied);

        var result = await UseCase().ExecuteAsync(new UnlockAccountCommand(_targetId), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ASuccessfulUnlock_RecordsTheAdministrativeActionOnly()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.Applied);

        await UseCase().ExecuteAsync(new UnlockAccountCommand(_targetId), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.AccountUnlockedByAdministrator(_targetId));
    }

    /// <summary>
    /// Unlike locking, lifting a lockout grants access back rather than taking it away, so there is
    /// no credential of the target's this needs to invalidate — see <c>UnlockAccountUseCase</c>.
    /// </summary>
    [Fact]
    public async Task ASuccessfulUnlock_RotatesNoStampAndRevokesNothing()
    {
        GivenTheOutcomeIs(LockoutChangeOutcome.Applied);

        await UseCase().ExecuteAsync(new UnlockAccountCommand(_targetId), TestToken);

        _securityEventLog.DidNotReceive().Record(SecurityEvent.SecurityStampRotated(_targetId));
    }

    private void GivenTheOutcomeIs(LockoutChangeOutcome outcome) =>
        _lockouts.UnlockAsync(_targetId, Arg.Any<CancellationToken>()).Returns(outcome);

    private UnlockAccountUseCase UseCase() => new(_lockouts, _securityEventLog, new UnlockAccountCommandValidator());
}
