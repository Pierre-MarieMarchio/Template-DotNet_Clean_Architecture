using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.LogoutEverywhere;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Sessions.UseCases.Commands.LogoutEverywhere;

public sealed class LogoutEverywhereUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACaller_RevokesEveryGrantForTheirOwnAccount()
    {
        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(_callerId, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACaller_RecordsTheRevocation()
    {
        await UseCase().ExecuteAsync(TestToken);

        _securityEventLog.Received(1).Record(SecurityEvent.RefreshTokenRevoked(_callerId));
    }

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

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(_callerId, cancellationToken: cancellation.Token);
    }

    private LogoutEverywhereUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_sessionTokens, _securityEventLog, currentUser);

    private LogoutEverywhereUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
