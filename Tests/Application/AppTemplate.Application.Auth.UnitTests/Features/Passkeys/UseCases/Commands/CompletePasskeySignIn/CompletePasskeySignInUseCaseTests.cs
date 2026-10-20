using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;

public sealed class CompletePasskeySignInUseCaseTests
{
    private static readonly Guid _userId = Guid.CreateVersion7();

    private readonly IPasskeyService _passkeys = Substitute.For<IPasskeyService>();
    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly StubCurrentSession _currentSession = StubCurrentSession.InSession(Guid.CreateVersion7());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABlankCredential_NeverReachesThePasskeyPort()
    {
        var result = await UseCase().ExecuteAsync(new CompletePasskeySignInCommand("", "the-state"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(AppTemplate.Application.Core.Common.Results.ErrorType.Validation);
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// One answer for every refusal an anonymous route can meet, so a guess is told nothing —
    /// the same reason every login refusal carries <c>auth.login.invalidCredentials</c>.
    /// </summary>
    [Fact]
    public async Task ACeremonyThatDoesNotVerify_IsRefusedWithTheOneSignInError()
    {
        _passkeys.CompleteSignInAsync("the-credential", "the-state", Arg.Any<CancellationToken>())
            .Returns(PasskeySignInOutcome.InvalidCeremony());

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.passkeySignIn.invalid");
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.Received(1).Record(
            Arg.Is<SecurityEvent>(e => e.Kind == SecurityEventKind.PasskeySignInFailed && e.UserId == null));
    }

    /// <summary>
    /// The assertion verified but its signature counter could not be persisted: sign-in stops here
    /// rather than completing against a credential that could be replayed.
    /// </summary>
    [Fact]
    public async Task AStoreRefusal_IsRefusedWithTheOneSignInError()
    {
        _passkeys.CompleteSignInAsync("the-credential", "the-state", Arg.Any<CancellationToken>())
            .Returns(PasskeySignInOutcome.StoreRejected());

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.passkeySignIn.invalid");
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// The same guard every other way in applies once the account is identified: a locked-out
    /// account answers its passkey the way it answers its password.
    /// </summary>
    [Fact]
    public async Task AnAccountThatMayNoLongerSignIn_IsRefusedAndNamedInTheTrail()
    {
        GivenTheAssertionSucceeded();
        _accounts.CanSignInAsync(_userId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.passkeySignIn.invalid");
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
        _securityEventLog.Received(1).Record(
            Arg.Is<SecurityEvent>(e => e.Kind == SecurityEventKind.PasskeySignInFailed && e.UserId == _userId));
    }

    /// <summary>
    /// The decision this vertical made about the armed second factor: a user-verified passkey is
    /// the whole of a sign-in — the ceremony's user-verification requirement is
    /// <c>required</c> — so no challenge is issued and the tokens are minted here, not after a
    /// second step. The factor still governs the ways in that prove less.
    /// </summary>
    [Fact]
    public async Task AnArmedSecondFactor_IsSatisfiedByThePasskeyAlone()
    {
        GivenTheAssertionSucceeded(twoFactorEnabled: true);
        _accounts.CanSignInAsync(_userId, Arg.Any<CancellationToken>()).Returns(true);
        GivenTheTokensAreIssued();

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<LoginOutcome.Authenticated>();
    }

    [Fact]
    public async Task AVerifiedPasskey_StartsOneSessionWithTheCallingClient()
    {
        GivenTheAssertionSucceeded();
        _accounts.CanSignInAsync(_userId, Arg.Any<CancellationToken>()).Returns(true);
        GivenTheTokensAreIssued();

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        var authenticated = result.Value.ShouldBeOfType<LoginOutcome.Authenticated>();
        authenticated.AccessToken.ShouldBe("the-access-token");
        authenticated.RefreshToken.ShouldBe("the-refresh-token");

        // The session the tokens name is the one this request's client started — the same pairing
        // a password sign-in makes.
        await _sessionTokens.Received(1).IssueRefreshTokenAsync(
            _userId, _currentSession.Client, Arg.Any<CancellationToken>());
        await _sessionTokens.Received(1).IssueAccessTokenAsync(
            _userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AVerifiedPasskey_RecordsTheSignInItCompleted()
    {
        GivenTheAssertionSucceeded();
        _accounts.CanSignInAsync(_userId, Arg.Any<CancellationToken>()).Returns(true);
        GivenTheTokensAreIssued();

        await UseCase().ExecuteAsync(ARequest(), TestToken);

        _securityEventLog.Received(1).Record(
            Arg.Is<SecurityEvent>(e => e.Kind == SecurityEventKind.PasskeySignInSucceeded && e.UserId == _userId));
    }

    private static CompletePasskeySignInCommand ARequest() => new("the-credential", "the-state");

    private void GivenTheTokensAreIssued()
    {
        _sessionTokens.IssueRefreshTokenAsync(_userId, Arg.Any<SessionClient>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedRefreshToken("the-refresh-token", DateTimeOffset.Parse("2026-10-11T12:00:00Z"), Guid.CreateVersion7()));
        _sessionTokens.IssueAccessTokenAsync(_userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedAccessToken("the-access-token", DateTimeOffset.Parse("2026-10-11T11:00:00Z")));
    }

    private void GivenTheAssertionSucceeded(bool twoFactorEnabled = false) =>
        _passkeys.CompleteSignInAsync("the-credential", "the-state", Arg.Any<CancellationToken>())
            .Returns(PasskeySignInOutcome.Authenticated(new AccountIdentity(
                _userId, "a@example.org", twoFactorEnabled)));

    private CompletePasskeySignInUseCase UseCase() =>
        new(
            _passkeys,
            _accounts,
            _sessionTokens,
            _securityEventLog,
            _currentSession,
            new CompletePasskeySignInCommandValidator());
}
