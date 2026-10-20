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

/// <summary>
/// Signing in with a provider is four decisions, not one path, and the happy path is the least
/// interesting of them: what matters is which of the four cases a given token lands in, and that a
/// caller learns nothing from a refusal about which one it was.
/// </summary>
public sealed class SignInWithExternalProviderUseCaseTests
{
    private const string _provider = "google";
    private const string _subject = "112233445566778899";
    private const string _email = "someone@example.com";

    private static readonly DateTimeOffset _accessTokenExpiry = DateTimeOffset.UnixEpoch.AddMinutes(15);
    private static readonly DateTimeOffset _refreshTokenExpiry = DateTimeOffset.UnixEpoch.AddDays(30);

    /// <summary>The client an external sign-in names its new session by, so assertions can see it travel.</summary>
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

    #region The token itself

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

    /// <summary>
    /// A forged token and a provider nobody configured answer identically. Mapping the unknown
    /// provider to its own error would let anyone enumerate which providers an installation accepts.
    /// </summary>
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

    /// <summary>
    /// The provider name from the request selects which issuer, audience and key set the token is
    /// checked against. Passing anything else — a default, or a name read from the token's own
    /// claims — would let a token minted by one provider be presented as another's.
    /// </summary>
    [Fact]
    public async Task TheTokenIsVerified_AgainstTheProviderTheRequestNamed()
    {
        GivenTheToken(null);

        await _useCase.ExecuteAsync(new SignInWithExternalProviderCommand("apple", "an-id-token"), TestToken);

        await _verifier.Received(1).VerifyAsync("apple", "an-id-token", Arg.Any<CancellationToken>());
    }

    #endregion

    #region Case 1 — the provider identity is already linked

    /// <summary>
    /// The regression this whole design exists to prevent. Apple returns an address on the first
    /// authorisation only, so every later token has none and claims nothing about one. Resolving by
    /// the subject means those tokens still sign in; resolving by the address means the second Apple
    /// sign-in of every user fails, in production, and never in a development account that has only
    /// ever authorised once.
    /// </summary>
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

    /// <summary>
    /// The other half of the same guarantee, and the one that stays red if someone "helpfully" adds an
    /// address lookup for a linked identity: once the pair is on file the address is not consulted at
    /// all, so it cannot make a sign-in fail and it cannot move an account to a different one.
    /// </summary>
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

    /// <summary>
    /// Two providers may hand out the same address, and one provider hands out the same address to
    /// whoever holds it now. The pair is the key, so a token whose subject nothing is linked to does
    /// not reach the linked account that shares its address.
    /// </summary>
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

    #endregion

    #region The address gate on a first link

    /// <summary>
    /// A provider that will not say it checked the address has told us who the caller is at that
    /// provider and nothing about who they are here. Reaching an account from it would let anyone who
    /// can set an unverified address on a provider account claim any address they like.
    /// </summary>
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

    #endregion

    #region Case 2 — no local account holds the address

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

    #endregion

    #region Case 3 — a confirmed local account holds the address

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

    /// <summary>
    /// The link is what the sign-in rests on afterwards, so a store that would not take it must not
    /// produce a token pair anyway.
    /// </summary>
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

    #endregion

    #region Case 4 — an unconfirmed local account holds the address

    /// <summary>
    /// The vector, stated as a test: someone registers an address they cannot read and never confirms
    /// it. Linking automatically would hand them the account its real owner is creating right now
    /// through their provider.
    /// </summary>
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

    #endregion

    #region The shared tail

    /// <summary>
    /// Every refusal along the four cases produces the same error, down to the message. Compared
    /// against each other rather than against a literal, so a change moving all of them to one *new*
    /// error is caught by the per-case assertions above and a change splitting them is caught here.
    /// </summary>
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

    /// <summary>
    /// A provider proves who the caller is; it does not say the account may still sign in. Without
    /// this check an administrator's lockout is undone by one round trip to Google.
    /// </summary>
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

    /// <summary>
    /// A second factor the owner armed is not waived by having proved an identity somewhere else —
    /// otherwise linking a provider becomes the way around it.
    /// </summary>
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

    [Fact]
    public async Task TheCancellationToken_ReachesEveryStep()
    {
        var provisioned = AnAccount();
        using var cancellation = new CancellationTokenSource();

        GivenTheToken(AVerifiedIdentity());
        _externalLogins.ProvisionAsync(_email, _provider, _subject, Arg.Any<CancellationToken>())
            .Returns(provisioned);

        await _useCase.ExecuteAsync(AValidRequest(), cancellation.Token);

        await _verifier.Received(1).VerifyAsync(_provider, "an-id-token", cancellation.Token);
        await _externalLogins.Received(1).FindByExternalLoginAsync(_provider, _subject, cancellation.Token);
        await _externalLogins.Received(1).FindByEmailAsync(_email, cancellation.Token);
        await _externalLogins.Received(1).ProvisionAsync(_email, _provider, _subject, cancellation.Token);
        await _accounts.Received(1).CanSignInAsync(provisioned.UserId, cancellation.Token);
        await _sessionTokens.Received(1).IssueAccessTokenAsync(provisioned.UserId, Arg.Any<Guid>(), cancellation.Token);
        await _sessionTokens.Received(1).IssueRefreshTokenAsync(provisioned.UserId, Arg.Any<SessionClient>(), cancellation.Token);
    }

    #endregion

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
