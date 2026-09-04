using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

public sealed class ConfirmTwoFactorSetupCommandValidatorTests
{
    private readonly ConfirmTwoFactorSetupCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new ConfirmTwoFactorSetupCommand("123456")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCode_IsRejected(string code)
    {
        var result = _validator.Validate(new ConfirmTwoFactorSetupCommand(code));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "Code");
    }
}
