using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.RevokeAccessKey;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Results;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.AccessKeys.UseCases.Commands.RevokeAccessKey;

public sealed class RevokeAccessKeyUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();
    private static readonly Guid _accessKeyId = Guid.CreateVersion7();

    private readonly IAccessKeysService _accessKeys = Substitute.For<IAccessKeysService>();
    private readonly ISecurityEventLog _securityEventLog = Substitute.For<ISecurityEventLog>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new RevokeAccessKeyCommand(_accessKeyId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyId_NeverReachesThePort()
    {
        var result = await UseCase().ExecuteAsync(new RevokeAccessKeyCommand(Guid.Empty), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        _accessKeys.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AKeyThePortDoesNotRevokeForTheCaller_IsAnsweredAsNotFound_AndRecordsNothing()
    {
        _accessKeys.RevokeAsync(_callerId, _accessKeyId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(new RevokeAccessKeyCommand(_accessKeyId), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.accessKey.notFound");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        _securityEventLog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheCallersOwnKey_IsRevokedInTheCallersName_AndTheFactRecorded()
    {
        _accessKeys.RevokeAsync(_callerId, _accessKeyId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await UseCase().ExecuteAsync(new RevokeAccessKeyCommand(_accessKeyId), TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _accessKeys.Received(1).RevokeAsync(_callerId, _accessKeyId, Arg.Any<CancellationToken>());
        _securityEventLog.Received(1).Record(
            Arg.Is<SecurityEvent>(e => e.Kind == SecurityEventKind.AccessKeyRevoked && e.UserId == _callerId));
    }

    private RevokeAccessKeyUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private RevokeAccessKeyUseCase UseCaseFor(StubCurrentUser caller) =>
        new(_accessKeys, _securityEventLog, caller, new RevokeAccessKeyCommandValidator());
}
