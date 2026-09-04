using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.Validators;

public sealed class ResendConfirmationEmailCommandValidatorTests
{
    private readonly ResendConfirmationEmailCommandValidator _validator = new();

    [Fact]
    public void APresentEmail_IsAccepted() =>
        _validator.Validate(new ResendConfirmationEmailCommand("someone@example.com")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankEmail_IsRejected(string email) =>
        _validator.Validate(new ResendConfirmationEmailCommand(email)).IsValid.ShouldBeFalse();
}
