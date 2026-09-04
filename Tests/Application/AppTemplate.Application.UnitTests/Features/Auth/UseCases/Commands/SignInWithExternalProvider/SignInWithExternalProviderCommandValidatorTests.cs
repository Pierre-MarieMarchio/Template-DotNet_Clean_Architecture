using AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

public sealed class SignInWithExternalProviderCommandValidatorTests
{
    private readonly SignInWithExternalProviderCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("", "an-id-token")]
    [InlineData("   ", "an-id-token")]
    [InlineData("google", "")]
    [InlineData("google", "   ")]
    public async Task AMissingField_IsRejected(string provider, string idToken)
    {
        var result = await _validator.ValidateAsync(
            new SignInWithExternalProviderCommand(provider, idToken),
            TestToken);

        result.IsValid.ShouldBeFalse();
    }

    /// <summary>
    /// Neither field has a shape asserted here. A provider name nobody configured and a token that is
    /// not a JWT are both refusals the verifier owns, and answering 400 for either would tell a caller
    /// which providers exist before it had to forge anything.
    /// </summary>
    [Theory]
    [InlineData("google", "not-a-jwt")]
    [InlineData("a-provider-nobody-configured", "a.b.c")]
    public async Task AWellFormedRequest_IsLeftForTheVerifierToRefuse(string provider, string idToken)
    {
        var result = await _validator.ValidateAsync(
            new SignInWithExternalProviderCommand(provider, idToken),
            TestToken);

        result.IsValid.ShouldBeTrue();
    }
}
