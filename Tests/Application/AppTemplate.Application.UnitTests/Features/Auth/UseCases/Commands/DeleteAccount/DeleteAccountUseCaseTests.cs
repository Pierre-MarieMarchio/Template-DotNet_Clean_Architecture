using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.AccountDeletion;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.UseCases.Commands.DeleteAccount;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.DeleteAccount;

public sealed class DeleteAccountUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IAccountDeletion _accountDeletion = Substitute.For<IAccountDeletion>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _accountDeletion.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _accountDeletion.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A more permanent version of the self-lockout <c>LockAccountUseCase</c> refuses: nothing survives to undo it.</summary>
    [Fact]
    public async Task ACallerTargetingThemselves_IsRefusedBeforeTouchingTheStore()
    {
        var result = await UseCaseFor(StubCurrentUser.WithId(_callerId))
            .ExecuteAsync(new DeleteAccountCommand(_callerId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.account.cannotDeleteSelf");
        _accountDeletion.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(AccountDeletionStatus.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(AccountDeletionStatus.Deleted);

        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ASuccessfulDeletion_RecordsTheAdministrativeAction()
    {
        GivenTheOutcomeIs(AccountDeletionStatus.Deleted);

        await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.AccountDeleted(_targetId));
    }

    [Fact]
    public async Task AStoreRefusal_RecordsNothing()
    {
        GivenTheOutcomeIs(AccountDeletionStatus.Rejected);

        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private void GivenTheOutcomeIs(AccountDeletionStatus outcome) =>
        _accountDeletion.DeleteAsync(_targetId, Arg.Any<CancellationToken>()).Returns(outcome);

    private DeleteAccountUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_accountDeletion, _securityEventLog, currentUser, new DeleteAccountCommandValidator());

    private DeleteAccountUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
