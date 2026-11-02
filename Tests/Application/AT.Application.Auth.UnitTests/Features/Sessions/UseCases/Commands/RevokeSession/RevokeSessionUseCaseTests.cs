using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Sessions.UseCases.Commands.RevokeSession;

public sealed class RevokeSessionUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ISessionsService _sessions = Substitute.For<ISessionsService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RevokeSessionCommand(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _sessions.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptySessionId_NeverReachesTheStore()
    {
        var result = await UseCase().ExecuteAsync(new RevokeSessionCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _sessions.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task SomebodyElsesSession_IsAnsweredAsNotFound()
    {
        _sessions.RevokeAsync(_callerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(new RevokeSessionCommand(Guid.CreateVersion7()), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("auth.session.notFound");
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCallersOwnSession_IsEnded()
    {
        var sessionId = Guid.CreateVersion7();
        _sessions.RevokeAsync(_callerId, sessionId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await UseCase().ExecuteAsync(new RevokeSessionCommand(sessionId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _securityEventLog.Received(1).Record(SecurityEvent.RefreshTokenRevoked(_callerId));
    }

    [Fact]
    public async Task ASessionAlreadyEnded_SucceedsAgain()
    {
        _sessions.RevokeAsync(_callerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await UseCase().ExecuteAsync(new RevokeSessionCommand(Guid.CreateVersion7()), TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    private RevokeSessionUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private RevokeSessionUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_sessions, _securityEventLog, currentUser, new RevokeSessionCommandValidator());
}
