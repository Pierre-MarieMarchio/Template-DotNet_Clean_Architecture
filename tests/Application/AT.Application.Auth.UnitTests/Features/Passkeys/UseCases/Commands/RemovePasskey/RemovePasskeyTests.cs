using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Commands.RemovePasskey;

public sealed class RemovePasskeyTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IPasskeyService _passkeys = Substitute.For<IPasskeyService>();
    private readonly ISessionTokensService _sessionTokens = Substitute.For<ISessionTokensService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private readonly StubCurrentSession _currentSession = StubCurrentSession.InSession(Guid.CreateVersion7());

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous, StubCurrentSession.WithoutSession).Handle(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyCredentialId_NeverReachesThePasskeyPort()
    {
        var result = await UseCase().Handle(new RemovePasskeyCommand([]), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(AppTemplate.Application.Core.Common.Results.ErrorType.Validation);
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACredentialTheCallerDoesNotHold_IsAnsweredAsNotFound()
    {
        _passkeys.RemoveAsync(_callerId, Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().Handle(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.passkey.notFound");
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
        _sessionTokens.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACredentialTheCallerHolds_IsRemovedAndTheFactRecorded()
    {
        _passkeys.RemoveAsync(_callerId, Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await UseCase().Handle(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _securityEventLog.Received(1).RecordAsync(
            Arg.Is<SecurityEvent>(e => e.Kind == SecurityEventKind.PasskeyRemoved && e.UserId == _callerId));
    }

    [Fact]
    public async Task ASuccessfulRemoval_RevokesEverySessionButTheCallersOwn()
    {
        GivenTheRemovalSucceeds();

        await UseCase().Handle(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(
            _callerId,
            _currentSession.SessionId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulRemovalFromATokenWithoutASession_RevokesEverySession()
    {
        GivenTheRemovalSucceeds();

        await UseCaseFor(StubCurrentUser.WithId(_callerId), StubCurrentSession.WithoutSession)
            .Handle(ARequest(), TestToken);

        await _sessionTokens.Received(1).RevokeAllRefreshTokensAsync(
            _callerId,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASuccessfulRemoval_MakesOneRevocationCall()
    {
        GivenTheRemovalSucceeds();

        await UseCase().Handle(ARequest(), TestToken);

        _sessionTokens.ReceivedCalls().Select(call => call.GetMethodInfo().Name)
            .ShouldBe([nameof(ISessionTokensService.RevokeAllRefreshTokensAsync)]);
    }

    [Fact]
    public async Task ASuccessfulRemoval_RecordsTheSecurityStampRotation()
    {
        GivenTheRemovalSucceeds();

        await UseCase().Handle(ARequest(), TestToken);

        await _securityEventLog.Received(1).RecordAsync(SecurityEvent.SecurityStampRotated(_callerId));
    }

    private static RemovePasskeyCommand ARequest() => new([1, 2, 3]);

    private void GivenTheRemovalSucceeds() =>
        _passkeys.RemoveAsync(_callerId, Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(true);

    private ValidatedHandler<RemovePasskeyCommand, Result> UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId), _currentSession);

    private ValidatedHandler<RemovePasskeyCommand, Result> UseCaseFor(StubCurrentUser caller, StubCurrentSession currentSession) =>
        new(new RemovePasskeyCommandHandler(_passkeys, _sessionTokens, _securityEventLog, caller, currentSession).Handle, new RemovePasskeyCommandValidator());
}
