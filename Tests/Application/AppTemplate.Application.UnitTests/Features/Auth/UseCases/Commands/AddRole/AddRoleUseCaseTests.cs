using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.UseCases.Commands.AddRole;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.AddRole;

public sealed class AddRoleUseCaseTests
{
    private const string _role = "Admin";
    private static readonly Guid _targetId = Guid.CreateVersion7();

    private readonly IRoleAssignmentsService _roles = Substitute.For<IRoleAssignmentsService>();
    private readonly IRefreshTokenGrantsService _refreshTokens = Substitute.For<IRefreshTokenGrantsService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMalformedRequest_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new AddRoleCommand(Guid.Empty, _role), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _roles.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyRole_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new AddRoleCommand(_targetId, string.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _roles.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownAccount_IsReportedAsNotFound()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(new AddRoleCommand(_targetId, _role), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task AKnownAccount_Succeeds()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Applied);

        var result = await UseCase().ExecuteAsync(new AddRoleCommand(_targetId, _role), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUnseededRole_ReportsTheStoresMessage()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Rejected("Role 'Ghost' does not exist."));

        var result = await UseCase().ExecuteAsync(new AddRoleCommand(_targetId, "Ghost"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["role"].ShouldContain("Role 'Ghost' does not exist.");
    }

    /// <summary>The gap this whole vertical exists to close: see <c>RoleAssignments</c>.</summary>
    [Fact]
    public async Task ASuccessfulGrant_RevokesEveryRefreshTokenForTheTarget()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Applied);

        await UseCase().ExecuteAsync(new AddRoleCommand(_targetId, _role), TestToken);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(_targetId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulGrant_RecordsBothTheAdministrativeActionAndTheStampRotation()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Applied);

        await UseCase().ExecuteAsync(new AddRoleCommand(_targetId, _role), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.RoleGranted(_targetId, _role));
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_targetId));
    }

    [Fact]
    public async Task AStoreRefusal_RevokesNothing()
    {
        GivenTheOutcomeIs(RoleAssignmentChangeOutcome.Rejected("already assigned"));

        await UseCase().ExecuteAsync(new AddRoleCommand(_targetId, _role), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    private void GivenTheOutcomeIs(RoleAssignmentChangeOutcome change) =>
        _roles.AddRoleAsync(_targetId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(change);

    private AddRoleUseCase UseCase() => new(_roles, _refreshTokens, _securityEventLog, new AddRoleCommandValidator());
}
