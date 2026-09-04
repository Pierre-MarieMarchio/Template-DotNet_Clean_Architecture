using AppTemplate.Application.Features.Auth.UseCases.Commands.RequestEmailChange;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.RequestEmailChange;

public sealed class RequestEmailChangeCommandValidatorTests
{
    private readonly RequestEmailChangeCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new RequestEmailChangeCommand("correct horse battery", "someone@example.com"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCurrentPassword_IsRejected(string currentPassword) =>
        _validator.Validate(new RequestEmailChangeCommand(currentPassword, "someone@example.com"))
            .IsValid.ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void AMalformedNewEmail_IsRejected(string newEmail) =>
        _validator.Validate(new RequestEmailChangeCommand("correct horse battery", newEmail))
            .IsValid.ShouldBeFalse();
}
