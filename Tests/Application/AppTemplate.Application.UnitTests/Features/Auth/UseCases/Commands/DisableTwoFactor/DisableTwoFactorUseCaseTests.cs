using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Application.Features.Auth.UseCases.Commands.DisableTwoFactor;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.DisableTwoFactor;

public sealed class DisableTwoFactorUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITwoFactorEnrollment _enrollment = Substitute.For<ITwoFactorEnrollment>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _enrollment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlankPassword_NeverReachesTheEnrollmentPort()
    {
        var result = await UseCase().ExecuteAsync(new DisableTwoFactorCommand(""), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _enrollment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AWrongPassword_IsRefused()
    {
        GivenTheOutcomeIs(TwoFactorDisable.IncorrectPassword);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["currentPassword"].ShouldContain("The current password is incorrect.");
    }

    [Fact]
    public async Task AWrongPassword_RevokesNoRefreshTokenAndRecordsNoEvent()
    {
        GivenTheOutcomeIs(TwoFactorDisable.IncorrectPassword);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARightPassword_Succeeds()
    {
        GivenTheOutcomeIs(TwoFactorDisable.Disabled);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ARightPassword_RecordsThatTwoFactorSignInIsNowOff()
    {
        GivenTheOutcomeIs(TwoFactorDisable.Disabled);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.TwoFactorDisabled(_callerId));
    }

    /// <summary>
    /// The security stamp already rotated inside <c>DisableAsync</c>; refresh tokens survive that
    /// rotation and are this use case's responsibility to revoke.
    /// </summary>
    [Fact]
    public async Task ARightPassword_RevokesEveryRefreshTokenForTheAccount()
    {
        GivenTheOutcomeIs(TwoFactorDisable.Disabled);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(_callerId, Arg.Any<CancellationToken>());
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    private static DisableTwoFactorCommand ARequest() => new("correct horse battery");

    private void GivenTheOutcomeIs(TwoFactorDisable disable) =>
        _enrollment.DisableAsync(_callerId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(disable);

    private DisableTwoFactorUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_enrollment, _refreshTokens, _securityEventLog, currentUser, new DisableTwoFactorCommandValidator());

    private DisableTwoFactorUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
