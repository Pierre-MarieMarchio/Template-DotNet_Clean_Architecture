using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Features.Auth.UseCases.Commands.VerifyTwoFactor;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.VerifyTwoFactor;

/// <summary>
/// The second step of a two-step login: redeeming a challenge is only half the work, the other half
/// being that both refusals — a spent challenge and a wrong code — must be indistinguishable from
/// outside, the same discipline <c>LoginUseCaseTests</c> applies to a first-step refusal.
/// </summary>
public sealed class VerifyTwoFactorUseCaseTests
{
    private static readonly DateTimeOffset _accessTokenExpiry = DateTimeOffset.UnixEpoch.AddMinutes(15);
    private static readonly DateTimeOffset _refreshTokenExpiry = DateTimeOffset.UnixEpoch.AddDays(30);

    private readonly ITwoFactorChallenge _challenges = Substitute.For<ITwoFactorChallenge>();
    private readonly IUserAccounts _accounts = Substitute.For<IUserAccounts>();
    private readonly IAccessTokenIssuer _accessTokens = Substitute.For<IAccessTokenIssuer>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly VerifyTwoFactorUseCase _useCase;

    public VerifyTwoFactorUseCaseTests() =>
        _useCase = new VerifyTwoFactorUseCase(
            _challenges,
            _accounts,
            _accessTokens,
            _refreshTokens,
            _securityEventLog,
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
        _challenges.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnknownOrExpiredChallenge_IsRefused()
    {
        _challenges.RedeemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.InvalidChallenge);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Unauthorized(
            "auth.login.invalidTwoFactorChallenge",
            "The two-factor challenge is invalid or has expired."));
    }

    /// <summary>
    /// A wrong code against a live challenge answers with the exact same error as an unknown or
    /// expired one — see the class doc for why.
    /// </summary>
    [Fact]
    public async Task AWrongCodeAgainstALiveChallenge_AnswersWithTheSameErrorAsAnInvalidChallenge()
    {
        var account = new AccountIdentity(Guid.CreateVersion7(), "someone", "someone@example.com", true);

        _challenges.RedeemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.InvalidCode(account));

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.Error.ShouldBe(Error.Unauthorized(
            "auth.login.invalidTwoFactorChallenge",
            "The two-factor challenge is invalid or has expired."));
    }

    [Fact]
    public async Task AWrongCodeAgainstALiveChallenge_IsStillRecordedForTheAuditTrail()
    {
        var account = new AccountIdentity(Guid.CreateVersion7(), "someone", "someone@example.com", true);

        _challenges.RedeemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.InvalidCode(account));

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.TwoFactorChallengeFailed(account.UserId));
    }

    [Fact]
    public async Task AnUnknownChallenge_MintsNoTokenAndRecordsNothing()
    {
        _challenges.RedeemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.InvalidChallenge);

        await _useCase.ExecuteAsync(ARequest(), TestToken);

        _accessTokens.ReceivedCalls().ShouldBeEmpty();
        _refreshTokens.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// A challenge issued before the account was locked out, disabled or deleted must not still mint
    /// tokens once redeemed — the same guard <c>RefreshAccessTokenUseCase</c> applies to a presented
    /// refresh token.
    /// </summary>
    [Fact]
    public async Task AVerifiedCodeForAnAccountThatCanNoLongerSignIn_IsRefused()
    {
        var account = GivenTheCodeIsVerified(usedRecoveryCode: false);
        _accounts.CanSignInAsync(account.UserId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.login.invalidTwoFactorChallenge");
        _accessTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AVerifiedCode_IsAnsweredWithBothTokensAndTheAccount()
    {
        var account = GivenTheCodeIsVerified(usedRecoveryCode: false);

        var result = await _useCase.ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        var authenticated = result.Value.ShouldBeOfType<LoginOutcome.Authenticated>();
        authenticated.UserId.ShouldBe(account.UserId);
        authenticated.UserName.ShouldBe(account.UserName);
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

    /// <summary>Worth its own fact: it is also the signal that one of the ten one-time codes is now gone.</summary>
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
        var account = new AccountIdentity(Guid.CreateVersion7(), "someone", "someone@example.com", true);

        _challenges.RedeemAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TwoFactorRedemptionOutcome.Verified(account, usedRecoveryCode));

        _accounts.CanSignInAsync(account.UserId, Arg.Any<CancellationToken>()).Returns(true);

        _accessTokens.IssueAsync(account.UserId, Arg.Any<CancellationToken>())
            .Returns(new IssuedAccessToken("access-token", _accessTokenExpiry));

        _refreshTokens.IssueAsync(account.UserId, Arg.Any<CancellationToken>())
            .Returns(new IssuedRefreshToken("refresh-token", _refreshTokenExpiry));

        return account;
    }
}
