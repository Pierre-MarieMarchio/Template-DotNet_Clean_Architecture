using AppTemplate.Application.Features.Auth.UseCases.Commands.ResendConfirmationEmail;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ResendConfirmationEmail;

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
