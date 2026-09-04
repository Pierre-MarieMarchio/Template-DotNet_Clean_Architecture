using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands;

public sealed class LogoutUseCaseTests
{
    private readonly IRefreshTokenGrants _refreshTokens = Substitute.For<IRefreshTokenGrants>();
    private readonly LogoutUseCase _useCase;

    public LogoutUseCaseTests() => _useCase = new LogoutUseCase(_refreshTokens, new LogoutRequestValidator());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// Signing out has to revoke a specific token, so a blank one is a request that could
    /// not end any session — it is refused before the revocation runs. Removing the
    /// <c>IsValid</c> check turns this red.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankRefreshToken_NeverReachesTheGrants(string refreshToken)
    {
        var result = await _useCase.ExecuteAsync(new LogoutRequest(refreshToken), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("auth.validation");
        _refreshTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task APresentedToken_IsRevoked()
    {
        var result = await _useCase.ExecuteAsync(new LogoutRequest("an-opaque-secret"), TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _refreshTokens.Received(1).RevokeAsync("an-opaque-secret", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Revocation is silent about whether the token existed, and so is this: an answer that differed
    /// would make signing out a way to test a token.
    /// </summary>
    [Fact]
    public async Task ATokenNobodyWasIssued_IsAnsweredTheSameWay()
    {
        var forKnown = await _useCase.ExecuteAsync(new LogoutRequest("an-opaque-secret"), TestToken);
        var forUnknown = await _useCase.ExecuteAsync(new LogoutRequest("never-issued"), TestToken);

        forUnknown.IsSuccess.ShouldBe(forKnown.IsSuccess);
        forUnknown.Error.ShouldBe(forKnown.Error);
    }

    /// <summary>Only the presented grant goes: signing out on one device is not signing out everywhere.</summary>
    [Fact]
    public async Task TheWholeFamily_IsNotRevoked()
    {
        await _useCase.ExecuteAsync(new LogoutRequest("an-opaque-secret"), TestToken);

        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_IsForwarded()
    {
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(new LogoutRequest("an-opaque-secret"), cancellation.Token);

        await _refreshTokens.Received(1).RevokeAsync("an-opaque-secret", cancellation.Token);
    }
}
