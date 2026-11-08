using AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;

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
