using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Login;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.Login;

/// <summary>
/// Logging in is a credential check followed by two token issuances, and the interesting part is
/// what happens when the check does not pass: every reason has to look the same from outside.
/// </summary>
public sealed class LoginUseCaseTests
{
    private static readonly DateTimeOffset _accessTokenExpiry = DateTimeOffset.UnixEpoch.AddMinutes(15);

    private static readonly DateTimeOffset _refreshTokenExpiry = DateTimeOffset.UnixEpoch.AddDays(30);

    private readonly IUserAccounts _accounts = Substitute.For<IUserAccounts>();
    private readonly IAccessTokenIssuer _accessTokens = Substitute.For<IAccessTokenIssuer>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ITwoFactorChallenge _twoFactorChallenge = Substitute.For<ITwoFactorChallenge>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly LoginUseCase _useCase;

    public LoginUseCaseTests() =>
        _useCase = new LoginUseCase(
            _accounts,
            _accessTokens,
            _refreshTokens,
            _twoFactorChallenge,
            _securityEventLog,
            new LoginCommandValidator());

    /// <summary>
    /// Every way the credential check can refuse. Read off the enum rather than listed, so a new
    /// outcome cannot be added without a decision about how it is answered.
    /// </summary>
    public static TheoryData<CredentialCheckStatus> Refusals =>
        [.. Enum.GetValues<CredentialCheckStatus>().Where(outcome => outcome is not CredentialCheckStatus.Verified)];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// The credential check must not be reached with a blank field, or a request with no password
    /// goes straight to the user store. Removing the <c>IsValid</c> check turns this red.
    /// </summary>
    [Theory]
    [InlineData("", "correct horse battery")]
    [InlineData("   ", "correct horse battery")]
    [InlineData("someone@example.com", "")]
    [InlineData("someone@example.com", "   ")]
    [InlineData("", "")]
    public async Task AnIncompleteRequest_NeverReachesTheCredentialCheck(string email, string password)
    {
        var result = await _useCase.ExecuteAsync(new LoginCommand(email, password), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        _accounts.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Login validates shape only. An address that is not a valid email is a credential
    /// that will not match, not a malformed request — answering 400 here would tell an
    /// attacker which addresses are even worth trying.
    /// </summary>
    [Fact]
    public async Task AMalformedEmail_IsLeftForTheCredentialCheckToRefuse()
    {
        GivenTheCredentialIsVerified();

        var result = await _useCase.ExecuteAsync(
            new LoginCommand("not-an-email", "correct horse battery"),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _accounts.Received(1).VerifyCredentialAsync(
            "not-an-email",
            "correct horse battery",
            Arg.Any<CancellationToken>());
    }

    #region Indistinguishability

    /// <summary>
    /// An unknown address, a wrong password, an unconfirmed address and a locked-out account all
    /// answer with one error, down to the message. Mapping any of them to its own error — a 403 for
    /// the lockout, a "confirm your email" hint — is what turns this endpoint into a user directory.
    /// </summary>
    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task EveryRefusal_AnswersWithTheSameError(CredentialCheckStatus outcome)
    {
        _accounts.VerifyCredentialAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CredentialCheckOutcome.Refused(outcome));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Unauthorized(
            "auth.login.invalidCredentials",
            "Email or password is incorrect."));
    }

    /// <summary>
    /// The failure is still recorded for the audit trail even though the caller is told nothing more
    /// than the one uniform error.
    /// </summary>
    [Fact]
    public async Task ARefusal_IsRecordedAsAnAuthenticationFailure()
    {
        _accounts.VerifyCredentialAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CredentialCheckOutcome.Refused(CredentialCheckStatus.IncorrectPassword));

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _securityEventLog.Received(1)
            .Record(SecurityEvent.AuthenticationFailed(null, CredentialCheckStatus.IncorrectPassword));
    }

    /// <summary>
    /// The theory above compares each refusal against a literal. This compares them against each
    /// other, so a change that moved all of them to the same *new* error would still be caught by one
    /// of the two.
    /// </summary>
    [Fact]
    public async Task NoRefusal_IsDistinguishableFromAnother()
    {
        var errors = new List<Error>();

        foreach (var outcome in Enum.GetValues<CredentialCheckStatus>())
        {
            if (outcome is CredentialCheckStatus.Verified)
            {
                continue;
            }

            _accounts.VerifyCredentialAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(CredentialCheckOutcome.Refused(outcome));

            var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

            errors.Add(result.Error.ShouldNotBeNull());
        }

        errors.Count.ShouldBeGreaterThan(1, "there is nothing to compare, so this proves nothing.");
        errors.Distinct().Count().ShouldBe(1, "the refusals differ, and the difference is measurable.");
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task ARefusedLogin_MintsNoToken(CredentialCheckStatus outcome)
    {
        _accounts.VerifyCredentialAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CredentialCheckOutcome.Refused(outcome));

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _accessTokens.ReceivedCalls().ShouldBeEmpty();
        _refreshTokens.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// A refusal carrying an account would be a caller's chance to use it anyway, so the guard reads
    /// both the outcome and the account. Loosening it to the outcome alone leaves this passing;
    /// loosening it to the account alone turns it red.
    /// </summary>
    [Fact]
    public async Task ARefusalCarryingAnAccount_IsStillARefusal()
    {
        _accounts.VerifyCredentialAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CredentialCheckOutcome(
                CredentialCheckStatus.LockedOut,
                new AccountIdentity(Guid.CreateVersion7(), "someone", "someone@example.com", TwoFactorEnabled: false)));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        _accessTokens.ReceivedCalls().ShouldBeEmpty();
    }

    #endregion

    #region A verified credential

    [Fact]
    public async Task AVerifiedCredential_IsAnsweredWithBothTokensAndTheAccount()
    {
        var account = GivenTheCredentialIsVerified();

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

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
    public async Task AVerifiedCredential_IsRecordedAsALoginSuccess()
    {
        var account = GivenTheCredentialIsVerified();

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.LoginSucceeded(account.UserId));
    }

    /// <summary>
    /// Both tokens are issued for the account the check returned, not for anything taken from the
    /// request — the request only ever held an address somebody typed.
    /// </summary>
    [Fact]
    public async Task BothTokens_AreIssuedForTheVerifiedAccount()
    {
        var account = GivenTheCredentialIsVerified();

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        await _accessTokens.Received(1).IssueAsync(account.UserId, Arg.Any<CancellationToken>());
        await _refreshTokens.Received(1).IssueAsync(account.UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_ReachesEveryStep()
    {
        var account = GivenTheCredentialIsVerified();
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(AValidRequest(), cancellation.Token);

        await _accounts.Received(1).VerifyCredentialAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            cancellation.Token);

        await _accessTokens.Received(1).IssueAsync(account.UserId, cancellation.Token);
        await _refreshTokens.Received(1).IssueAsync(account.UserId, cancellation.Token);
    }

    #endregion

    #region A two-factor account

    /// <summary>
    /// A verified password is only half a login for a two-factor account: a challenge comes back
    /// instead of a token pair, and neither token issuer is touched.
    /// </summary>
    [Fact]
    public async Task AVerifiedCredentialOnATwoFactorAccount_IsAnsweredWithAChallengeInstead()
    {
        var account = GivenTheCredentialIsVerified(twoFactorEnabled: true);

        _twoFactorChallenge.IssueAsync(account.UserId, Arg.Any<CancellationToken>())
            .Returns(new IssuedTwoFactorChallenge("a-challenge-token", DateTimeOffset.UnixEpoch.AddMinutes(5)));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        var challenge = result.Value.ShouldBeOfType<LoginOutcome.TwoFactorRequired>();
        challenge.ChallengeToken.ShouldBe("a-challenge-token");
        _accessTokens.ReceivedCalls().ShouldBeEmpty();
        _refreshTokens.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Not recorded as a login yet: only half of it happened. <c>VerifyTwoFactorUseCase</c> records
    /// <see cref="SecurityEvent.LoginSucceeded"/> once the second step actually completes.
    /// </summary>
    [Fact]
    public async Task AVerifiedCredentialOnATwoFactorAccount_IsNotYetRecordedAsALoginSuccess()
    {
        var account = GivenTheCredentialIsVerified(twoFactorEnabled: true);

        _twoFactorChallenge.IssueAsync(account.UserId, Arg.Any<CancellationToken>())
            .Returns(new IssuedTwoFactorChallenge("a-challenge-token", DateTimeOffset.UnixEpoch.AddMinutes(5)));

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _securityEventLog.DidNotReceive().Record(SecurityEvent.LoginSucceeded(account.UserId));
    }

    #endregion

    private static LoginCommand AValidRequest() => new("someone@example.com", "correct horse battery");

    private AccountIdentity GivenTheCredentialIsVerified(bool twoFactorEnabled = false)
    {
        var account = new AccountIdentity(Guid.CreateVersion7(), "someone", "someone@example.com", twoFactorEnabled);

        _accounts.VerifyCredentialAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CredentialCheckOutcome.Verified(account));

        _accessTokens.IssueAsync(account.UserId, Arg.Any<CancellationToken>())
            .Returns(new IssuedAccessToken("access-token", _accessTokenExpiry));

        _refreshTokens.IssueAsync(account.UserId, Arg.Any<CancellationToken>())
            .Returns(new IssuedRefreshToken("refresh-token", _refreshTokenExpiry));

        return account;
    }
}
