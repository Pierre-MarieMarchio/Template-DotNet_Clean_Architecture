using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RequestPasswordReset;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.RequestPasswordReset;

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
