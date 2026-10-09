using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactor;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.SetUpTwoFactor;
using AppTemplate.Application.Auth.UnitTests.TestDoubles;
using AppTemplate.Application.Core.Common.Ports;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Auth.UseCases.Commands.SetUpTwoFactor;

public sealed class SetUpTwoFactorUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITwoFactorService _twoFactor = Substitute.For<ITwoFactorService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _twoFactor.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AFreshEnrollment_AnswersWithTheSharedKeyAndTheAuthenticatorUri()
    {
        _twoFactor.BeginSetupAsync(_callerId, Arg.Any<CancellationToken>())
            .Returns(TwoFactorSetupOutcome.Started(
                "ABCDEFGH",
                "otpauth://totp/AppTemplate:someone@example.com?secret=ABCDEFGH&issuer=AppTemplate&digits=6"));

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SharedKey.ShouldBe("ABCDEFGH");
        result.Value.AuthenticatorUri.ShouldBe(
            "otpauth://totp/AppTemplate:someone@example.com?secret=ABCDEFGH&issuer=AppTemplate&digits=6");
    }

    /// <summary>
    /// Provisioning a second secret over a live one would hand back a key none of the account's
    /// existing authenticator apps were built from, with no warning that the old one is about to stop
    /// being checked — so this is refused rather than allowed.
    /// </summary>
    [Fact]
    public async Task AnAlreadyEnabledAccount_IsRefused()
    {
        _twoFactor.BeginSetupAsync(_callerId, Arg.Any<CancellationToken>()).Returns(TwoFactorSetupOutcome.AlreadyEnabled);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.twoFactor.alreadyEnabled");
    }

    /// <summary>A token that outlived its account — see <c>GetCurrentUserUseCase</c>.</summary>
    [Fact]
    public async Task AnAccountThatNoLongerExists_IsAnsweredAsUnauthenticated()
    {
        _twoFactor.BeginSetupAsync(_callerId, Arg.Any<CancellationToken>()).Returns(TwoFactorSetupOutcome.NoSuchAccount);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AStoreRefusal_IsReportedAsSuch()
    {
        _twoFactor.BeginSetupAsync(_callerId, Arg.Any<CancellationToken>()).Returns(TwoFactorSetupOutcome.Rejected);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.twoFactor.setupRejected");
    }

    [Fact]
    public async Task TheCancellationToken_ReachesTheTwoFactorPort()
    {
        using var cancellation = new CancellationTokenSource();

        _twoFactor.BeginSetupAsync(_callerId, Arg.Any<CancellationToken>()).Returns(TwoFactorSetupOutcome.Started("key", "uri"));

        await UseCase().ExecuteAsync(cancellation.Token);

        await _twoFactor.Received(1).BeginSetupAsync(_callerId, cancellation.Token);
    }

    private SetUpTwoFactorUseCase UseCaseFor(ICurrentUser currentUser) => new(_twoFactor, currentUser);

    private SetUpTwoFactorUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
