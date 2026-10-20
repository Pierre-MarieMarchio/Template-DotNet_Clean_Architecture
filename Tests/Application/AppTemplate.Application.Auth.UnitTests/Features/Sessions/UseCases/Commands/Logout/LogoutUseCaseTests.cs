using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Sessions.UseCases.Commands.Logout;

public sealed class LogoutUseCaseTests
{
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();
    private readonly LogoutUseCase _useCase;

    public LogoutUseCaseTests() =>
        _useCase = new LogoutUseCase(_sessionTokens, _securityEventLog, new LogoutCommandValidator());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankRefreshToken_NeverReachesTheGrants(string refreshToken)
    {
        var result = await _useCase.ExecuteAsync(new LogoutCommand(refreshToken), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task APresentedToken_IsRevoked()
    {
        var result = await _useCase.ExecuteAsync(new LogoutCommand("an-opaque-secret"), TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _sessionTokens.Received(1).RevokeRefreshTokenAsync("an-opaque-secret", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARevokedToken_IsRecordedAsALogout()
    {
        var userId = Guid.CreateVersion7();
        _sessionTokens.RevokeRefreshTokenAsync("an-opaque-secret", Arg.Any<CancellationToken>()).Returns(userId);

        await _useCase.ExecuteAsync(new LogoutCommand("an-opaque-secret"), TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.LoggedOut(userId));
    }

    [Fact]
    public async Task ATokenNobodyWasIssued_RecordsNothing()
    {
        _sessionTokens.RevokeRefreshTokenAsync("never-issued", Arg.Any<CancellationToken>()).Returns((Guid?)null);

        await _useCase.ExecuteAsync(new LogoutCommand("never-issued"), TestToken);

        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ATokenNobodyWasIssued_IsAnsweredTheSameWay()
    {
        var forKnown = await _useCase.ExecuteAsync(new LogoutCommand("an-opaque-secret"), TestToken);
        var forUnknown = await _useCase.ExecuteAsync(new LogoutCommand("never-issued"), TestToken);

        forUnknown.IsSuccess.ShouldBe(forKnown.IsSuccess);
        forUnknown.Error.ShouldBe(forKnown.Error);
    }

    [Fact]
    public async Task TheWholeFamily_IsNotRevoked()
    {
        await _useCase.ExecuteAsync(new LogoutCommand("an-opaque-secret"), TestToken);

        await _sessionTokens.DidNotReceiveWithAnyArgs().RevokeAllRefreshTokensAsync(default, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_IsForwarded()
    {
        using var cancellation = new CancellationTokenSource();

        await _useCase.ExecuteAsync(new LogoutCommand("an-opaque-secret"), cancellation.Token);

        await _sessionTokens.Received(1).RevokeRefreshTokenAsync("an-opaque-secret", cancellation.Token);
    }
}
