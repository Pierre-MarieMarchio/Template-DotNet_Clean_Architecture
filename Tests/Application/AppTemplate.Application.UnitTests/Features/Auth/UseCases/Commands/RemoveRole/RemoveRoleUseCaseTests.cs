using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RemoveRole;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.RemoveRole;

public sealed class RemoveRoleUseCaseTests
{
    private const string _role = "Admin";
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IRoleAssignments _roles = Substitute.For<IRoleAssignments>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RemoveRoleCommand(_targetId, _role), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _roles.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new RemoveRoleCommand(Guid.Empty, _role), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _roles.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Not narrowed to "may not remove their own Administrator role": this layer has no reference to
    /// that literal — see <c>RemoveRoleUseCase</c> — so the guard refuses any role removed from the
    /// caller's own account, proved here with a role that is not <c>Admin</c>.
    /// </summary>
    [Fact]
    public async Task ACallerTargetingThemselves_IsRefusedRegardlessOfWhichRole()
    {
        var result = await UseCaseFor(StubCurrentUser.WithId(_callerId))
            .ExecuteAsync(new RemoveRoleCommand(_callerId, "SomeOtherRole"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.roles.cannotTargetSelf");
        _roles.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(new RemoveRoleCommand(_targetId, _role), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Applied);

        var result = await UseCase().ExecuteAsync(new RemoveRoleCommand(_targetId, _role), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ARoleTheAccountDoesNotCarry_ReportsTheStoresMessage()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Rejected("User is not in role 'Admin'."));

        var result = await UseCase().ExecuteAsync(new RemoveRoleCommand(_targetId, _role), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["role"].ShouldContain("User is not in role 'Admin'.");
    }

    /// <summary>The gap this whole vertical exists to close: see <c>RoleAssignments</c>.</summary>
    [Fact]
    public async Task ASuccessfulRevocation_RevokesEveryRefreshTokenForTheTarget()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Applied);

        await UseCase().ExecuteAsync(new RemoveRoleCommand(_targetId, _role), TestToken);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(_targetId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulRevocation_RecordsBothTheAdministrativeActionAndTheStampRotation()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Applied);

        await UseCase().ExecuteAsync(new RemoveRoleCommand(_targetId, _role), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.RoleRevoked(_targetId, _role));
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_targetId));
    }

    [Fact]
    public async Task AStoreRefusal_RevokesNothing()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Rejected("not in role"));

        await UseCase().ExecuteAsync(new RemoveRoleCommand(_targetId, _role), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private void GivenTheOutcomeIs(RoleAssignmentChangeOutcome change) =>
        _roles.RemoveRoleAsync(_targetId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(change);

    private RemoveRoleUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_roles, _refreshTokens, _securityEventLog, currentUser, new RemoveRoleCommandValidator());

    private RemoveRoleUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
