using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.BeginPasskeySignIn;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Commands.BeginPasskeySignIn;

public sealed class BeginPasskeySignInUseCaseTests
{
    private readonly IPasskeyService _passkeys = Substitute.For<IPasskeyService>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheChallengeThePortBuilds_IsHandedBackUnchanged()
    {
        var challenge = new PasskeySignInChallenge("{\"rpId\":\"localhost\"}", "the-state");
        _passkeys.BeginSignInAsync(Arg.Any<CancellationToken>()).Returns(challenge);

        var result = await new BeginPasskeySignInUseCase(_passkeys).ExecuteAsync(TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(challenge);
    }
}
