using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactor;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DisableTwoFactor;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Auth.UseCases.Commands.DisableTwoFactor;

public sealed class DisableTwoFactorUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITwoFactorService _twoFactor = Substitute.For<ITwoFactorService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous, StubCurrentSession.WithoutSession).ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _twoFactor.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ABlankPassword_NeverReachesTheTwoFactorPort()
    {
        var result = await UseCase().ExecuteAsync(new DisableTwoFactorCommand(""), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _twoFactor.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>A token that outlived its account — see <c>GetCurrentUserUseCase</c>.</summary>
    [Fact]
    public async Task AnAccountThatNoLongerExists_IsAnsweredAsUnauthenticatedAndRevokesNothing()
    {
        GivenTheOutcomeIs(TwoFactorDisableStatus.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AWrongPassword_IsRefused()
    {
        GivenTheOutcomeIs(TwoFactorDisableStatus.IncorrectPassword);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Details!["currentPassword"].ShouldContain("The current password is incorrect.");
    }

    [Fact]
    public async Task AWrongPassword_RevokesNoRefreshTokenAndRecordsNoEvent()
    {
        GivenTheOutcomeIs(TwoFactorDisableStatus.IncorrectPassword);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// The factor is still on, so nothing about the account's posture changed: no event, and no
    /// session revoked for a change that did not happen.
    /// </summary>
    [Fact]
    public async Task AStoreRefusal_IsReportedAndRevokesNothingAndRecordsNothing()
    {
        GivenTheOutcomeIs(TwoFactorDisableStatus.Rejected);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.twoFactor.disableRejected");
        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARightPassword_Succeeds()
    {
        GivenTheOutcomeIs(TwoFactorDisableStatus.Disabled);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ARightPassword_RecordsThatTwoFactorSignInIsNowOff()
    {
        GivenTheOutcomeIs(TwoFactorDisableStatus.Disabled);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.TwoFactorDisabled(_callerId));
    }

    /// <summary>
    /// The security stamp already rotated inside <c>DisableAsync</c>; refresh tokens survive that
    /// rotation and are this use case's responsibility to revoke. The caller's own session, which
    /// just proved the password, is the one spared.
    /// </summary>
    [Fact]
    public async Task ARightPassword_RevokesEverySessionButTheCallersOwn()
    {
        GivenTheOutcomeIs(TwoFactorDisableStatus.Disabled);

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(
            _callerId,
            _currentSession.SessionId,
            Arg.Any<CancellationToken>());
        _securityEventLog.Received(1).Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    /// <summary>
    /// A token that names no session — issued before sessions were named in it — spares nothing:
    /// an unknown session is not one to spare.
    /// </summary>
    [Fact]
    public async Task ARightPasswordFromATokenWithoutASession_RevokesEverySession()
    {
        GivenTheOutcomeIs(TwoFactorDisableStatus.Disabled);

        await UseCaseFor(StubCurrentUser.WithId(_callerId), StubCurrentSession.WithoutSession)
            .ExecuteAsync(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(
            _callerId,
            null,
            Arg.Any<CancellationToken>());
    }

    private static DisableTwoFactorCommand ARequest() => new("correct horse battery");

    private void GivenTheOutcomeIs(TwoFactorDisableStatus disable) =>
        _twoFactor.DisableAsync(_callerId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(disable);

    /// <summary>The session the caller disarms the factor from, fixed so assertions can name it.</summary>
    private readonly StubCurrentSession _currentSession = StubCurrentSession.InSession(Guid.CreateVersion7());

    private DisableTwoFactorUseCase UseCaseFor(ICurrentUser currentUser, StubCurrentSession currentSession) =>
        new(_twoFactor, _sessionTokens, _securityEventLog, currentUser, currentSession, new DisableTwoFactorCommandValidator());

    private DisableTwoFactorUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId), _currentSession);
}
