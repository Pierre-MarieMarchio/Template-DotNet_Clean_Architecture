using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;

public sealed class CompletePasskeySignInCommandValidatorTests
{
    private readonly CompletePasskeySignInCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new CompletePasskeySignInCommand("{\"id\":\"a-credential\"}", "the-state"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("", "the-state")]
    [InlineData("   ", "the-state")]
    public void ABlankCredential_IsRejected(string credential, string authenticationState)
    {
        var result = _validator.Validate(new CompletePasskeySignInCommand(credential, authenticationState));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "Credential");
    }

    [Theory]
    [InlineData("{\"id\":\"a-credential\"}", "")]
    [InlineData("{\"id\":\"a-credential\"}", "   ")]
    public void ABlankAuthenticationState_IsRejected(string credential, string authenticationState)
    {
        var result = _validator.Validate(new CompletePasskeySignInCommand(credential, authenticationState));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "AuthenticationState");
    }
}
