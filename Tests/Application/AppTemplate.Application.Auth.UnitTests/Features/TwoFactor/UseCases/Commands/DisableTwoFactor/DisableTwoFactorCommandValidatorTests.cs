using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.DisableTwoFactor;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.TwoFactor.UseCases.Commands.DisableTwoFactor;

public sealed class DisableTwoFactorCommandValidatorTests
{
    private readonly DisableTwoFactorCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new DisableTwoFactorCommand("correct horse battery")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCurrentPassword_IsRejected(string currentPassword)
    {
        var result = _validator.Validate(new DisableTwoFactorCommand(currentPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "CurrentPassword");
    }
}
