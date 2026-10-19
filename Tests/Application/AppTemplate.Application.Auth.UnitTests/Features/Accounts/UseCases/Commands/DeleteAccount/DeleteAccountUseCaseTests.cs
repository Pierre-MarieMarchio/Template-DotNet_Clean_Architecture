using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteAccount;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.DeleteAccount;

public sealed class DeleteAccountUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IAccountAdministrationService _administration = Substitute.For<IAccountAdministrationService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly IDomainEventPublisher _domainEvents = Substitute.For<IDomainEventPublisher>();
    private readonly StubDateTimeProvider _dateTimeProvider = new();

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

    /// <summary>
    /// The rows another feature owns under the id are that feature's to remove, and the fact is what
    /// tells them: an administrative deletion that skipped it would leave them orphaned again.
    /// </summary>
    [Fact]
    public async Task ASuccessfulDeletion_PublishesTheAccountDeletedFact()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        await _domainEvents.Received(1).PublishAsync(
            Arg.Is<UserAccountDeleted>(fact =>
                fact.UserId.Value == _targetId && fact.OccurredAt == _dateTimeProvider.UtcNow),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoreRefusal_RecordsNothingAndPublishesNothing()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Rejected);

        var result = await UseCase().ExecuteAsync(new DeleteAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
        _domainEvents.ReceivedCalls().ShouldBeEmpty();
    }

    private void GivenTheOutcomeIs(AccountAdministrationOutcome outcome) =>
        _administration.DeleteAsync(_targetId, Arg.Any<CancellationToken>()).Returns(outcome);

    private DeleteAccountUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_administration, _securityEventLog, _domainEvents, currentUser, _dateTimeProvider, new DeleteAccountCommandValidator());

    private DeleteAccountUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
