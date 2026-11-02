using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.VerifyTwoFactor;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.TwoFactor.UseCases.Commands.VerifyTwoFactor;

public sealed class VerifyTwoFactorUseCaseTests
{
    private static readonly DateTimeOffset _accessTokenExpiry = DateTimeOffset.UnixEpoch.AddMinutes(15);
    private static readonly DateTimeOffset _refreshTokenExpiry = DateTimeOffset.UnixEpoch.AddDays(30);

    private static readonly SessionClient _client = new("test-agent/1.0");

    private readonly ITwoFactorService _twoFactor = Substitute.For<ITwoFactorService>();
    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly VerifyTwoFactorUseCase _useCase;

    public VerifyTwoFactorUseCaseTests() =>
        _useCase = new VerifyTwoFactorUseCase(
            _twoFactor,
            _accounts,
            _sessionTokens,
            _securityEventLog,
            new StubCurrentSession(null, _client),
            new VerifyTwoFactorCommandValidator());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("", "123456")]
    [InlineData("a-challenge-token", "")]
    public async Task AnIncompleteRequest_NeverReachesTheChallengePort(string challengeToken, string code)
    {
        var result = await _useCase.ExecuteAsync(new VerifyTwoFactorCommand(challengeToken, code), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _twoFactor.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownOrExpiredChallenge_IsRefused()
    {
        _twoFactor.RedeemChallengeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.InvalidChallenge);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Unauthorized(
            "auth.login.invalidTwoFactorChallenge",
            "The two-factor challenge is invalid or has expired."));
    }

    [Fact]
    public async Task AWrongCodeAgainstALiveChallenge_AnswersWithTheSameErrorAsAnInvalidChallenge()
    {
        var account = new AccountIdentity(Guid.CreateVersion7(), "someone@example.com", true);

        _twoFactor.RedeemChallengeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.InvalidCode(account));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.Error.ShouldBe(Error.Unauthorized(
            "auth.login.invalidTwoFactorChallenge",
            "The two-factor challenge is invalid or has expired."));
    }

    [Fact]
    public async Task AWrongCodeAgainstALiveChallenge_IsStillRecordedForTheAuditTrail()
    {
        var account = new AccountIdentity(Guid.CreateVersion7(), "someone@example.com", true);

        _twoFactor.RedeemChallengeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.InvalidCode(account));

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.TwoFactorChallengeFailed(account.UserId));
    }

    [Fact]
    public async Task AnUnknownChallenge_MintsNoTokenAndRecordsNothing()
    {
        _twoFactor.RedeemChallengeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.InvalidChallenge);

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AVerifiedCodeForAnAccountThatCanNoLongerSignIn_IsRefused()
    {
        var account = GivenTheCodeIsVerified(usedRecoveryCode: false);
        _accounts.CanSignInAsync(account.UserId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.login.invalidTwoFactorChallenge");
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AVerifiedCode_IsAnsweredWithBothTokensAndTheAccount()
    {
        var account = GivenTheCodeIsVerified(usedRecoveryCode: false);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        var authenticated = result.Value.ShouldBeOfType<LoginOutcome.Authenticated>();
        authenticated.UserId.ShouldBe(account.UserId);
        authenticated.Email.ShouldBe(account.Email);
        authenticated.AccessToken.ShouldBe("access-token");
        authenticated.AccessTokenExpiresAt.ShouldBe(_accessTokenExpiry);
        authenticated.RefreshToken.ShouldBe("refresh-token");
        authenticated.RefreshTokenExpiresAt.ShouldBe(_refreshTokenExpiry);
    }

    [Fact]
    public async Task AVerifiedCode_IsRecordedAsALoginSuccess()
    {
        var account = GivenTheCodeIsVerified(usedRecoveryCode: false);

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.LoginSucceeded(account.UserId));
    }

    [Fact]
    public async Task AVerifiedCodeFromTheAuthenticatorApp_DoesNotRecordARecoveryCodeRedemption()
    {
        var account = GivenTheCodeIsVerified(usedRecoveryCode: false);

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.DidNotReceive().Record(SecurityEvent.RecoveryCodeRedeemed(account.UserId));
    }

    [Fact]
    public async Task AVerifiedRecoveryCode_IsAlsoRecordedAsARecoveryCodeRedemption()
    {
        var account = GivenTheCodeIsVerified(usedRecoveryCode: true);

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.RecoveryCodeRedeemed(account.UserId));
    }

    private static VerifyTwoFactorCommand ARequest() => new("a-challenge-token", "123456");

    private AccountIdentity GivenTheCodeIsVerified(bool usedRecoveryCode)
    {
        var account = new AccountIdentity(Guid.CreateVersion7(), "someone@example.com", true);

        _twoFactor.RedeemChallengeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.Verified(account, usedRecoveryCode));

        _accounts.CanSignInAsync(account.UserId, Arg.Any<CancellationToken>()).Returns(true);

        _sessionTokens.IssueAccessTokenAsync(account.UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedAccessToken("access-token", _accessTokenExpiry));

        _sessionTokens.IssueRefreshTokenAsync(account.UserId, Arg.Any<SessionClient>(), Arg.Any<CancellationToken>())
            .Returns(ARefreshToken());

        return account;
    }

    private static IssuedRefreshToken ARefreshToken() => new("refresh-token", _refreshTokenExpiry, Guid.CreateVersion7());
}
