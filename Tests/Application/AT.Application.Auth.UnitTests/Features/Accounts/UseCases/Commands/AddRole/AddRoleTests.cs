using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.AddRole;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.AddRole;

public sealed class AddRoleTests
{
    private const string _role = "Admin";
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IAccountAdministrationService _administration = Substitute.For<IAccountAdministrationService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().Handle(new AddRoleCommand(Guid.Empty, _role), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyRole_NeverReachesTheStore()
    {
        var result = await UseCase().Handle(new AddRoleCommand(_targetId, string.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _administration.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.NoSuchAccount);

        var result = await UseCase().Handle(new AddRoleCommand(_targetId, _role), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        var result = await UseCase().Handle(new AddRoleCommand(_targetId, _role), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUnseededRole_ReportsTheStoresMessage()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.RejectedBecause("Role 'Ghost' does not exist."));

        var result = await UseCase().Handle(new AddRoleCommand(_targetId, "Ghost"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["role"].ShouldContain("Role 'Ghost' does not exist.");
    }

    [Fact]
    public async Task ASuccessfulGrant_RevokesEveryRefreshTokenForTheTarget()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        await UseCase().Handle(new AddRoleCommand(_targetId, _role), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(_targetId, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulGrant_RecordsBothTheAdministrativeActionAndTheStampRotation()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.Applied);

        await UseCase().Handle(new AddRoleCommand(_targetId, _role), TestToken);

        await _securityEventLog.Received(1).RecordAsync(SecurityEvent.RoleGranted(_targetId, _role));
        await _securityEventLog.Received(1).RecordAsync(SecurityEvent.SecurityStampRotated(_targetId));
    }

    [Fact]
    public async Task AStoreRefusal_RevokesNothing()
    {
        GivenTheOutcomeIs(AccountAdministrationOutcome.RejectedBecause("already assigned"));

        await UseCase().Handle(new AddRoleCommand(_targetId, _role), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private void GivenTheOutcomeIs(AccountAdministrationOutcome change) =>
        _administration.GrantRoleAsync(_targetId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(change);

    private ValidatedHandler<AddRoleCommand, Result> UseCase() => new(new AddRoleCommandHandler(_administration, _sessionTokens, _securityEventLog).Handle, new AddRoleCommandValidator());
}
