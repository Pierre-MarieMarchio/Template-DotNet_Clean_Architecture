using AppTemplate.Application.Auth.Features.Auth.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RemovePasskey;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Auth.UseCases.Commands.RemovePasskey;

public sealed class RemovePasskeyUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly IPasskeyService _passkeys = Substitute.For<IPasskeyService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyCredentialId_NeverReachesThePasskeyPort()
    {
        var result = await UseCase().ExecuteAsync(new RemovePasskeyCommand([]), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(AppTemplate.Application.Core.Common.Results.ErrorType.Validation);
        _passkeys.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>
    /// Somebody else's passkey, an unknown id and an already-removed one are the same answer, so
    /// the caller learns nothing about which credential ids exist.
    /// </summary>
    [Fact]
    public async Task ACredentialTheCallerDoesNotHold_IsAnsweredAsNotFound()
    {
        _passkeys.RemoveAsync(_callerId, Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.passkey.notFound");
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ACredentialTheCallerHolds_IsRemovedAndTheFactRecorded()
    {
        _passkeys.RemoveAsync(_callerId, Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await UseCase().ExecuteAsync(ARequest(), TestToken);

        result.IsSuccess.ShouldBeTrue();
        _securityEventLog.Received(1).Record(
            Arg.Is<SecurityEvent>(e => e.Kind == SecurityEventKind.PasskeyRemoved && e.UserId == _callerId));
    }

    private static RemovePasskeyCommand ARequest() => new([1, 2, 3]);

    private RemovePasskeyUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private RemovePasskeyUseCase UseCaseFor(StubCurrentUser caller) =>
        new(_passkeys, _securityEventLog, caller, new RemovePasskeyCommandValidator());
}
