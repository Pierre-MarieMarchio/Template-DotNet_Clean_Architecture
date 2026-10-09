using AppTemplate.Application.Auth.Features.Auth.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DeleteAccount;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Auth.UseCases.Commands.DeleteAccount;

public sealed class DeleteAccountUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IAccountAdministrationService _administration = Substitute.For<IAccountAdministrationService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A more permanent version of the self-lockout <c>LockAccountUseCase</c> refuses: nothing survives to undo it.</summary>
    [Fact]
    public async Task ACallerTargetingThemselves_IsRefusedBeforeTouchingTheStore()
    {
        var result = await UseCaseFor(StubCurrentUser.WithId(_callerId))
            .ExecuteAsync(new DeleteAccountCommand(_callerId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.cannotDeleteSelf");
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ASuccessfulDeletion_RecordsTheAdministrativeAction()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.AccountDeleted(_targetId));
    }

    [Fact]
    public async Task AStoreRefusal_RecordsNothing()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Rejected);

        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private void GivenTheOutcomeIs(AccountAdministrationOutcome outcome) =>
        _administration.DeleteAsync(_targetId, Arg.Any<CancellationToken>()).Returns(outcome);

    private DeleteAccountUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_administration, _securityEventLog, currentUser, new DeleteAccountCommandValidator());

    private DeleteAccountUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
