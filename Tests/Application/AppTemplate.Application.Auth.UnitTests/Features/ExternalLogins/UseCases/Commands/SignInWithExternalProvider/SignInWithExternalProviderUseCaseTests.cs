using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;
using AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;

public sealed class SignInWithExternalProviderUseCaseTests
{
    private const string _provider = "google";
    private const string _subject = "112233445566778899";
    private const string _email = "someone@example.com";

    private static readonly DateTimeOffset _accessTokenExpiry = DateTimeOffset.UnixEpoch.AddMinutes(15);
    private static readonly DateTimeOffset _refreshTokenExpiry = DateTimeOffset.UnixEpoch.AddDays(30);

    private static readonly SessionClient _client = new("test-agent/1.0");

    private static readonly Error _refusal = Error.Unauthorized(
        "auth.externalSignIn.refused",
        "The external sign-in could not be completed.");

    private readonly IExternalIdentityVerifier _verifier = Substitute.For<IExternalIdentityVerifier>();
    private readonly IExternalLoginsService _externalLogins = Substitute.For<IExternalLoginsService>();
    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ITwoFactorService _twoFactor = Substitute.For<ITwoFactorService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly SignInWithExternalProviderUseCase _useCase;

    public SignInWithExternalProviderUseCaseTests()
    {
        _useCase = new SignInWithExternalProviderUseCase(
            _verifier,
            _externalLogins,
            _accounts,
            _sessionTokens,
            _twoFactor,
            _securityEventLog,
            new StubCurrentSession(null, _client),
            new SignInWithExternalProviderCommandValidator());

        _sessionTokens.IssueAccessTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedAccessToken("access-token", _accessTokenExpiry));

        _sessionTokens.IssueRefreshTokenAsync(Arg.Any<Guid>(), Arg.Any<SessionClient>(), Arg.Any<CancellationToken>())
            .Returns(_refreshToken);

        _accounts.CanSignInAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("", "an-id-token")]
    [InlineData("google", "")]
    [InlineData("   ", "   ")]
    public async Task AnIncompleteRequest_NeverReachesTheVerifier(string provider, string idToken)
    {
        var result = await _useCase.ExecuteAsync(
            new SignInWithExternalProviderCommand(provider, idToken),
            TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _verifier.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ATokenThatDoesNotVerify_IsRefusedWithoutTouchingAnyAccount()
    {
        GivenTheToken(null);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_refusal);
        _externalLogins.ReceivedCalls().ShouldBeEmpty();
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheTokenIsVerified_AgainstTheProviderTheRequestNamed()
    {
        GivenTheToken(null);

        await _useCase.ExecuteAsync(new SignInWithExternalProviderCommand("apple", "an-id-token"), TestToken);

        await _verifier.Received(1).VerifyAsync("apple", "an-id-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAlreadyLinkedIdentity_SignsIn_EvenWhenTheTokenCarriesNoAddressAtAll()
    {
        var account = AnAccount();

        GivenTheToken(new VerifiedExternalIdentity(_provider, _subject, Email: null, EmailVerified: false));

        _externalLogins.FindByExternalLoginAsync(_provider, _subject, Arg.Any<CancellationToken>())
            .Returns(account);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<SignInWithExternalProviderOutcome.Authenticated>()
            .UserId.ShouldBe(account.UserId);
    }

    [Fact]
    public async Task AnAlreadyLinkedIdentity_IsNeverResolvedByAddress()
    {
        GivenTheToken(AVerifiedIdentity());

        _externalLogins.FindByExternalLoginAsync(_provider, _subject, Arg.Any<CancellationToken>())
            .Returns(AnAccount());

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        await _externalLogins.DidNotReceive().FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _externalLogins.DidNotReceive()
            .LinkAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _externalLogins.DidNotReceive()
            .ProvisionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADifferentSubjectAtTheSameAddress_IsADifferentIdentity()
    {
        var linked = AnAccount();

        _externalLogins.FindByExternalLoginAsync(_provider, _subject, Arg.Any<CancellationToken>())
            .Returns(linked);

        GivenTheToken(new VerifiedExternalIdentity(_provider, "a-different-subject", _email, EmailVerified: true));

        var provisioned = AnAccount();
        _externalLogins.ProvisionAsync(_email, _provider, "a-different-subject", Arg.Any<CancellationToken>())
            .Returns(provisioned);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<SignInWithExternalProviderOutcome.Authenticated>()
            .UserId.ShouldBe(provisioned.UserId);
    }

    [Theory]
    [InlineData(_email, false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("   ", true)]
    public async Task AFirstLink_WithoutAVerifiedAddress_IsRefused(string? email, bool emailVerified)
    {
        GivenTheToken(new VerifiedExternalIdentity(_provider, _subject, email, emailVerified));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_refusal);
        await _externalLogins.DidNotReceive().FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _externalLogins.DidNotReceive()
            .ProvisionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAddressNobodyHolds_ProvisionsAConfirmedPasswordlessAccountAndSignsItIn()
    {
        var provisioned = AnAccount();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.ProvisionAsync(_email, _provider, _subject, Arg.Any<CancellationToken>())
            .Returns(provisioned);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        var authenticated = result.Value.ShouldBeOfType<SignInWithExternalProviderOutcome.Authenticated>();
        authenticated.UserId.ShouldBe(provisioned.UserId);
        authenticated.Email.ShouldBe(provisioned.Email);
        authenticated.AccessToken.ShouldBe("access-token");
        authenticated.AccessTokenExpiresAt.ShouldBe(_accessTokenExpiry);
        authenticated.RefreshToken.ShouldBe("refresh-token");
        authenticated.RefreshTokenExpiresAt.ShouldBe(_refreshTokenExpiry);
        authenticated.AccountCreated.ShouldBeTrue();
    }

    [Fact]
    public async Task AProvisionedAccount_IsRecordedAsARegistrationAndAsALogin()
    {
        var provisioned = AnAccount();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.ProvisionAsync(_email, _provider, _subject, Arg.Any<CancellationToken>())
            .Returns(provisioned);

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.Registered(provisioned.UserId));
        _securityEventLog.Received(1).Record(SecurityEvent.LoginSucceeded(provisioned.UserId));
    }

    [Fact]
    public async Task ARefusedProvisioning_MintsNoToken()
    {
        GivenTheToken(AVerifiedIdentity());
        _externalLogins.ProvisionAsync(_email, _provider, _subject, Arg.Any<CancellationToken>())
            .Returns((AccountIdentity?)null);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_refusal);
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AConfirmedLocalAccount_IsLinkedAndSignedIn()
    {
        var account = AnAccount();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.FindByEmailAsync(_email, Arg.Any<CancellationToken>())
            .Returns(new LocalAccountMatch(account, EmailConfirmed: true));
        _externalLogins.LinkAsync(account.UserId, _provider, _subject, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        var authenticated = result.Value.ShouldBeOfType<SignInWithExternalProviderOutcome.Authenticated>();
        authenticated.UserId.ShouldBe(account.UserId);
        authenticated.AccountCreated.ShouldBeFalse();

        await _externalLogins.Received(1).LinkAsync(account.UserId, _provider, _subject, Arg.Any<CancellationToken>());
        await _externalLogins.DidNotReceive()
            .ProvisionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARefusedLink_MintsNoToken()
    {
        var account = AnAccount();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.FindByEmailAsync(_email, Arg.Any<CancellationToken>())
            .Returns(new LocalAccountMatch(account, EmailConfirmed: true));
        _externalLogins.LinkAsync(account.UserId, _provider, _subject, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_refusal);
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnconfirmedLocalAccount_IsNeitherLinkedNorSignedIn()
    {
        var account = AnAccount();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.FindByEmailAsync(_email, Arg.Any<CancellationToken>())
            .Returns(new LocalAccountMatch(account, EmailConfirmed: false));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_refusal);
        await _externalLogins.DidNotReceive()
            .LinkAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _externalLogins.DidNotReceive()
            .ProvisionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARefusedLink_IsRecordedAgainstTheAccountItWasRefusedFor()
    {
        var account = AnAccount();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.FindByEmailAsync(_email, Arg.Any<CancellationToken>())
            .Returns(new LocalAccountMatch(account, EmailConfirmed: false));

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        _securityEventLog.Received(1).Record(
            SecurityEvent.AuthenticationFailed(account.UserId, CredentialCheckStatus.EmailNotConfirmed));
    }

    [Fact]
    public async Task NoRefusal_IsDistinguishableFromAnother()
    {
        var errors = new List<Error>
        {
            await RefusalFrom(() => GivenTheToken(null)),

            await RefusalFrom(() => GivenTheToken(new VerifiedExternalIdentity(_provider, _subject, _email, EmailVerified: false))),

            await RefusalFrom(() =>
            {
                GivenTheToken(AVerifiedIdentity());
                _externalLogins.FindByEmailAsync(_email, Arg.Any<CancellationToken>())
                    .Returns(new LocalAccountMatch(AnAccount(), EmailConfirmed: false));
            }),

            await RefusalFrom(() =>
            {
                GivenTheToken(AVerifiedIdentity());
                _externalLogins.FindByExternalLoginAsync(_provider, _subject, Arg.Any<CancellationToken>())
                    .Returns(AnAccount());
                _accounts.CanSignInAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
            }),
        };

        errors.Count.ShouldBeGreaterThan(1, "there is nothing to compare, so this proves nothing.");
        errors.Distinct().Count().ShouldBe(1, "the refusals differ, and the difference is measurable.");
    }

    [Fact]
    public async Task AnAccountThatMayNoLongerSignIn_IsRefusedDespiteAValidToken()
    {
        var account = AnAccount();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.FindByExternalLoginAsync(_provider, _subject, Arg.Any<CancellationToken>())
            .Returns(account);
        _accounts.CanSignInAsync(account.UserId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_refusal);
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.Received(1).Record(
            SecurityEvent.AuthenticationFailed(account.UserId, CredentialCheckStatus.LockedOut));
    }

    [Fact]
    public async Task AnAccountWithTwoFactorArmed_IsChallengedInsteadOfIssuedTokens()
    {
        var account = AnAccount(twoFactorEnabled: true);

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.FindByExternalLoginAsync(_provider, _subject, Arg.Any<CancellationToken>())
            .Returns(account);
        _twoFactor.IssueChallengeAsync(account.UserId, Arg.Any<CancellationToken>())
            .Returns(new IssuedTwoFactorChallenge("a-challenge-token", DateTimeOffset.UnixEpoch.AddMinutes(5)));

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<SignInWithExternalProviderOutcome.TwoFactorRequired>()
            .ChallengeToken.ShouldBe("a-challenge-token");

        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.DidNotReceive().Record(SecurityEvent.LoginSucceeded(account.UserId));
    }

    [Fact]
    public async Task BothTokens_AreIssuedForTheResolvedAccount_NotForAnythingInTheRequest()
    {
        var account = AnAccount();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.FindByExternalLoginAsync(_provider, _subject, Arg.Any<CancellationToken>())
            .Returns(account);

        await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        await _sessionTokens.Received(1).IssueRefreshTokenAsync(account.UserId, _client, Arg.Any<CancellationToken>());
        await _sessionTokens.Received(1).IssueAccessTokenAsync(
            account.UserId,
            _refreshToken.SessionId,
            Arg.Any<CancellationToken>());
    }

    private static SignInWithExternalProviderCommand AValidRequest() => new(_provider, "an-id-token");

    private static VerifiedExternalIdentity AVerifiedIdentity() =>
        new(_provider, _subject, _email, EmailVerified: true);

    private static AccountIdentity AnAccount(bool twoFactorEnabled = false) =>
        new(Guid.CreateVersion7(), _email, twoFactorEnabled);

    private static readonly IssuedRefreshToken _refreshToken = new("refresh-token", _refreshTokenExpiry, Guid.CreateVersion7());

    private void GivenTheToken(VerifiedExternalIdentity? identity) =>
        _verifier.VerifyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(identity);

    private async Task<Error> RefusalFrom(Action arrange)
    {
        arrange();

        var result = await _useCase.ExecuteAsync(AValidRequest(), TestToken);

        result.IsFailure.ShouldBeTrue();

        return result.Error!;
    }
}
