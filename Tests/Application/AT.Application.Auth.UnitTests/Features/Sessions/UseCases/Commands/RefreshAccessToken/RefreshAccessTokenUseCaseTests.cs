using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Sessions.UseCases.Commands.RefreshAccessToken;

public sealed class RefreshAccessTokenUseCaseTests
{
    private static readonly DateTimeOffset _accessTokenExpiry = DateTimeOffset.UnixEpoch.AddMinutes(15);

    private static readonly DateTimeOffset _refreshTokenExpiry = DateTimeOffset.UnixEpoch.AddDays(30);

    private readonly IAccountsService _accounts = Substitute.For<IAccountsService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static readonly SessionClient _client = new("test-agent/1.0");

    private readonly RefreshAccessTokenUseCase _useCase;

    public RefreshAccessTokenUseCaseTests() =>
        _useCase = new RefreshAccessTokenUseCase(
            _accounts,
            _sessionTokens,
            _securityEventLog,
            new StubCurrentSession(null, _client),
            new RefreshAccessTokenCommandValidator());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

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
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ARejectedRotation_IsAnsweredWithOneErrorAndMintsNothing()
    {
        _sessionTokens.RotateRefreshTokenAsync("already-used", Arg.Any<SessionClient>(), Arg.Any<CancellationToken>())
            .Returns(RefreshTokenRotation.Rejected);

        var result = await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("already-used"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(Error.Unauthorized(
            "auth.refreshToken.invalid",
            "The refresh token is invalid or has expired."));

        await _sessionTokens.DidNotReceiveWithAnyArgs().IssueAccessTokenAsync(default, default, Arg.Any<CancellationToken>());
        _accounts.ReceivedCalls().ShouldBeEmpty("a rejected token names no account to ask about.");
    }

    [Fact]
    public async Task ARotatedGrantForAnAccountThatMayNoLongerSignIn_IsRefusedAndTakesTheFamilyWithIt()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.refreshToken.invalid");

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(userId, cancellationToken: Arg.Any<CancellationToken>());
        await _sessionTokens.DidNotReceiveWithAnyArgs().IssueAccessTokenAsync(default, default, Arg.Any<CancellationToken>());
    }

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

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoSecondGrant_IsIssued()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(true);

        await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().IssueRefreshTokenAsync(default, Arg.Any<SessionClient>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheGrantIsConsumed_BeforeTheAccountIsRevalidated()
    {
        var userId = GivenTheGrantRotates();
        _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>()).Returns(true);

        await _useCase.ExecuteAsync(new RefreshAccessTokenCommand("presented"), TestToken);

        Received.InOrder(() =>
        {
            _sessionTokens.RotateRefreshTokenAsync("presented", Arg.Any<SessionClient>(), Arg.Any<CancellationToken>());
            _accounts.CanSignInAsync(userId, Arg.Any<CancellationToken>());
            _sessionTokens.IssueAccessTokenAsync(userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
    }

    private Guid GivenTheGrantRotates()
    {
        var userId = Guid.CreateVersion7();

        _sessionTokens.RotateRefreshTokenAsync("presented", Arg.Any<SessionClient>(), Arg.Any<CancellationToken>())
            .Returns(RefreshTokenRotation.Rotated(
                userId,
                new IssuedRefreshToken("successor", _refreshTokenExpiry, Guid.CreateVersion7())));

        _sessionTokens.IssueAccessTokenAsync(userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new IssuedAccessToken("access-token", _accessTokenExpiry));

        return userId;
    }
}
