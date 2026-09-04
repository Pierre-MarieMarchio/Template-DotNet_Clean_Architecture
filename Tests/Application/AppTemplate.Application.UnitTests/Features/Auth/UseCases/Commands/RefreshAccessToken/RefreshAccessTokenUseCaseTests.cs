using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.RefreshAccessToken;

/// <summary>
/// A refresh consumes the presented grant first and only then asks whether the account may still
/// sign in — and when it may not, the successor it was just issued has to go too.
/// </summary>
public sealed class RefreshAccessTokenUseCaseTests
{
    private static readonly DateTimeOffset _accessTokenExpiry = DateTimeOffset.UnixEpoch.AddMinutes(15);

    private static readonly DateTimeOffset _refreshTokenExpiry = DateTimeOffset.UnixEpoch.AddDays(30);

    private readonly IUserAccounts _accounts = Substitute.For<IUserAccounts>();
    private readonly IAccessTokenIssuer _accessTokens = Substitute.For<IAccessTokenIssuer>();
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly RefreshAccessTokenUseCase _useCase;

    public RefreshAccessTokenUseCaseTests() =>
        _useCase = new RefreshAccessTokenUseCase(
            _accounts,
            _accessTokens,
            _refreshTokens,
            _securityEventLog,
            new RefreshAccessTokenCommandValidator());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// A blank token must not reach rotation: rotation treats reuse as theft, and an empty string is
    /// not a token anyone was ever issued. Removing the <c>IsValid</c> check turns this red.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankRefreshToken_NeverReachesRotation(string refreshToken)
    {
        var result = await _useCase.ExecuteAsync(new RefreshAccessTokenCommand(refreshToken), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        result.Error.Details!["refreshToken"].ShouldContain("Refresh token is required.");
        _refreshTokens.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Unknown, expired, revoked and replayed are one answer. The adapter has already revoked the
    /// family for a replay; the caller must not be able to tell that apart from a typo.
    /// </summary>
    [Fact]
    public async Task ARejectedRotation_IsAnsweredWithOneErrorAndMintsNothing()
    {
        _refreshTokens.RotateAsync("already-used", Arg.Any<CancellationToken>())
            .Returns(RefreshTokenRotation.Rejected);

        var result = await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("already-used"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Unauthorized(
            "auth.refreshToken.invalid",
            "The refresh token is invalid or has expired."));

        _accessTokens.ReceivedCalls().ShouldBeEmpty();
        _accounts.ReceivedCalls().ShouldBeEmpty("a rejected token names no account to ask about.");
    }

    /// <summary>
    /// A grant issued before the account was locked out or disabled must not keep minting access
    /// tokens, and the successor this very call produced has to be revoked with the rest — otherwise
    /// the holder simply refreshes again with it.
    /// </summary>
    [Fact]
    public async Task ARotatedGrantForAnAccountThatMayNoLongerSignIn_IsRefusedAndTakesTheFamilyWithIt()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.refreshToken.invalid");

        await _refreshTokens.Received(1).RevokeAllForUserAsync(userId, Arg.Any<CancellationToken>());
        _accessTokens.ReceivedCalls().ShouldBeEmpty("a refused refresh must not mint an access token.");
    }

    /// <summary>
    /// Deciding that a grant may not be renewed is this use case's job, so the resulting revocation
    /// is what it records — not the adapter that merely carries out the decision.
    /// </summary>
    [Fact]
    public async Task ARevokedFamily_IsRecordedAsARevocation()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(false);

        await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.RefreshTokenRevoked(userId));
    }

    [Fact]
    public async Task ARotatedGrantForAnActiveAccount_YieldsANewPair()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AccessToken.ShouldBe("access-token");
        result.Value.AccessTokenExpiresAt.ShouldBe(_accessTokenExpiry);
        result.Value.RefreshToken.ShouldBe("successor");
        result.Value.RefreshTokenExpiresAt.ShouldBe(_refreshTokenExpiry);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The successor comes from rotation, never from a fresh issuance: minting a second grant here
    /// would leave two live chains for one presented token.
    /// </summary>
    [Fact]
    public async Task NoSecondGrant_IsIssued()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(true);

        await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().IssueAsync(default, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Rotation is single-use, so it has to be the first thing that happens: revalidating the account
    /// first would leave a window in which a replayed token is answered by a check that passes.
    /// </summary>
    [Fact]
    public async Task TheGrantIsConsumed_BeforeTheAccountIsRevalidated()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(true);

        await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), TestToken);

        Received.InOrder(() =>
        {
            _refreshTokens.RotateAsync("presented", Arg.Any<CancellationToken>());
            _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>());
            _accessTokens.IssueAsync(userId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task TheCancellationToken_ReachesEveryStep()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), cancellation.Token);

        await _refreshTokens.Received(1).RotateAsync("presented", cancellation.Token);
        await _accounts.Received(1).CanSignInAsync(userId, cancellation.Token);
        await _accessTokens.Received(1).IssueAsync(userId, cancellation.Token);
    }

    private Guid GivenTheGrantRotates()
    {
        var userId = Guid.CreateVersion7();

        _refreshTokens.RotateAsync("presented", Arg.Any<CancellationToken>())
            .Returns(RefreshTokenRotation.Rotated(
                userId,
                new IssuedRefreshToken("successor", _refreshTokenExpiry)));

        _accessTokens.IssueAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new IssuedAccessToken("access-token", _accessTokenExpiry));

        return userId;
    }
}
