using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;

public sealed class CompletePasskeyRegistrationCommandValidatorTests
{
    private readonly CompletePasskeyRegistrationCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new CompletePasskeyRegistrationCommand(
            "{\"id\":\"a-credential\"}", "the-state", "YubiKey 5", "the password")).IsValid.ShouldBeTrue();

    [Fact]
    public void NoPasswordAtAll_IsAccepted() =>
        _validator.Validate(new CompletePasskeyRegistrationCommand(
            "{\"id\":\"a-credential\"}", "the-state", null, null)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("", "the-state")]
    [InlineData("   ", "the-state")]
    public void ABlankCredential_IsRejected(string credential, string registrationState)
    {
        var result = _validator.Validate(new CompletePasskeyRegistrationCommand(
            credential, registrationState, null, "the password"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "Credential");
    }

    [Theory]
    [InlineData("{\"id\":\"a-credential\"}", "")]
    [InlineData("{\"id\":\"a-credential\"}", "   ")]
    public void ABlankRegistrationState_IsRejected(string credential, string registrationState)
    {
        var result = _validator.Validate(new CompletePasskeyRegistrationCommand(
            credential, registrationState, null, "the password"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "RegistrationState");
    }

    [Fact]
    public void ANameLongerThanTheBound_IsRejected()
    {
        var result = _validator.Validate(new CompletePasskeyRegistrationCommand(
            "{\"id\":\"a-credential\"}", "the-state", new string('x', 129), "the password"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "Name");
    }

    [Fact]
    public void APasswordLongerThanTheBound_IsRejected()
    {
        var result = _validator.Validate(new CompletePasskeyRegistrationCommand(
            "{\"id\":\"a-credential\"}", "the-state", null, new string('x', 1025)));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "CurrentPassword");
    }
}
