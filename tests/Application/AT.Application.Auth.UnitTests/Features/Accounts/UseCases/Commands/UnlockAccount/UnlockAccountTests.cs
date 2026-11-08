using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.UnlockAccount;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.UnlockAccount;

public sealed class UnlockAccountTests
{
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IAccountAdministrationService _administration = Substitute.For<IAccountAdministrationService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().Handle(new UnlockAccountCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.NoSuchAccount);

        var result = await UseCase().Handle(new UnlockAccountCommand(_targetId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        var result = await UseCase().Handle(new UnlockAccountCommand(_targetId), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ASuccessfulUnlock_RecordsTheAdministrativeActionOnly()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        await UseCase().Handle(new UnlockAccountCommand(_targetId), TestToken);

        await _securityEventLog.Received(1).RecordAsync(SecurityEvent.AccountUnlockedByAdministrator(_targetId));
    }

    [Fact]
    public async Task ASuccessfulUnlock_RotatesNoStampAndRevokesNothing()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        await UseCase().Handle(new UnlockAccountCommand(_targetId), TestToken);

        await _securityEventLog.DidNotReceive().RecordAsync(SecurityEvent.SecurityStampRotated(_targetId));
    }

    private void GivenTheOutcomeIs(AccountAdministrationOutcome outcome) =>
        _administration.UnlockAsync(_targetId, Arg.Any<CancellationToken>()).Returns(outcome);

    private ValidatedHandler<UnlockAccountCommand, Result> UseCase() => new(new UnlockAccountCommandHandler(_administration, _securityEventLog).Handle, new UnlockAccountCommandValidator());
}
