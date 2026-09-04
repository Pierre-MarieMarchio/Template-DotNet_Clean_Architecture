using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.Validators;

public sealed class RequestPasswordResetCommandValidatorTests
{
    private readonly RequestPasswordResetCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new RequestPasswordResetCommand("someone@example.com")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankEmail_IsRejected(string email) =>
        _validator.Validate(new RequestPasswordResetCommand(email)).IsValid.ShouldBeFalse();
}
