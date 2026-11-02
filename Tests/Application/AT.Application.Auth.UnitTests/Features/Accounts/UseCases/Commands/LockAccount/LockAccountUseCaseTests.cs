using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.LockAccount;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.LockAccount;

public sealed class LockAccountUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IAccountAdministrationService _administration = Substitute.For<IAccountAdministrationService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new LockAccountCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACallerTargetingThemselves_IsRefusedBeforeTouchingTheStore()
    {
        var result = await UseCaseFor(StubCurrentUser.WithId(_callerId))
            .ExecuteAsync(new LockAccountCommand(_callerId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.lockout.cannotTargetSelf");
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        var result = await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ASuccessfulLock_RevokesEveryRefreshTokenForTheTarget()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(_targetId, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulLock_RecordsBothTheAdministrativeActionAndTheStampRotation()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.AccountLockedByAdministrator(_targetId));
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_targetId));
    }

    [Fact]
    public async Task AStoreRefusal_RevokesNothing()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Rejected);

        var result = await UseCase().ExecuteAsync(new LockAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private void GivenTheOutcomeIs(AccountAdministrationOutcome outcome) =>
        _administration.LockAsync(_targetId, Arg.Any<CancellationToken>()).Returns(outcome);

    private LockAccountUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_administration, _sessionTokens, _securityEventLog, currentUser, new LockAccountCommandValidator());

    private LockAccountUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
