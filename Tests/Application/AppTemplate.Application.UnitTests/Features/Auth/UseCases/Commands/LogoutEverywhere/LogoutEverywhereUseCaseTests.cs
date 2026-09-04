using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.UseCases.Commands.LogoutEverywhere;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.LogoutEverywhere;

public sealed class LogoutEverywhereUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IRefreshTokenGrantsService _refreshTokens = Substitute.For<IRefreshTokenGrantsService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _refreshTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACaller_RevokesEveryGrantForTheirOwnAccount()
    {
        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _refreshTokens.Received(1).RevokeAllForUserAsync(_callerId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACaller_RecordsTheRevocation()
    {
        await UseCase().ExecuteAsync(TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.RefreshTokenRevoked(_callerId));
    }

    /// <summary>
    /// The caller's own access token must keep working: they asked to end other sessions, not this
    /// one, and rotating the stamp would do both.
    /// </summary>
    [Fact]
    public async Task ACaller_DoesNotRotateTheSecurityStamp()
    {
        await UseCase().ExecuteAsync(TestToken);

        _securityEventLog.DidNotReceive().Record(SecurityEvent.SecurityStampRotated(_callerId));
    }

    [Fact]
    public async Task TheCancellationToken_IsForwarded()
    {
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(cancellation.Token);

        await _refreshTokens.Received(1).RevokeAllForUserAsync(_callerId, cancellation.Token);
    }

    private LogoutEverywhereUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_refreshTokens, _securityEventLog, currentUser);

    private LogoutEverywhereUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
