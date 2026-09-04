using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Application.Features.Auth.UseCases.Commands.SetUpTwoFactor;
using AppTemplate.Application.UnitTests.TestDoubles;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.SetUpTwoFactor;

public sealed class SetUpTwoFactorUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITwoFactorEnrollmentService _enrollment = Substitute.For<ITwoFactorEnrollmentService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous).ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.required");
        _enrollment.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task AFreshEnrollment_AnswersWithTheSharedKeyAndTheAuthenticatorUri()
    {
        _enrollment.BeginAsync(_callerId, Arg.Any<CancellationToken>())
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
        _enrollment.BeginAsync(_callerId, Arg.Any<CancellationToken>()).Returns(TwoFactorSetupOutcome.AlreadyEnabled);

        var result = await UseCase().ExecuteAsync(TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("auth.twoFactor.alreadyEnabled");
    }

    [Fact]
    public async Task TheCancellationToken_ReachesTheEnrollmentPort()
    {
        using var cancellation = new CancellationTokenSource();

        _enrollment.BeginAsync(_callerId, Arg.Any<CancellationToken>()).Returns(TwoFactorSetupOutcome.Started("key", "uri"));

        await UseCase().ExecuteAsync(cancellation.Token);

        await _enrollment.Received(1).BeginAsync(_callerId, cancellation.Token);
    }

    private SetUpTwoFactorUseCase UseCaseFor(ICurrentUser currentUser) => new(_enrollment, currentUser);

    private SetUpTwoFactorUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));
}
