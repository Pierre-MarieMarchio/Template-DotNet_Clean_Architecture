using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResetPassword;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Auth.UseCases.Commands.ResetPassword;

public sealed class ResetPasswordCommandValidatorTests
{
    private readonly ResetPasswordCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new ResetPasswordCommand("someone@example.com", "a-token", "correct horse battery"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankEmail_IsRejected(string email) =>
        ShouldFailOn(new ResetPasswordCommand(email, "a-token", "correct horse battery"), "Email");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankToken_IsRejected(string token) =>
        ShouldFailOn(new ResetPasswordCommand("someone@example.com", token, "correct horse battery"), "Token");

    /// <summary>The length policy is the store's — see <c>RegisterCommandValidatorTests</c>.</summary>
    [Fact]
    public void AShortNewPassword_IsLeftToTheStore() =>
        _validator.Validate(new ResetPasswordCommand("someone@example.com", "a-token", "a")).IsValid.ShouldBeTrue();

    [Fact]
    public void EveryBrokenField_IsReportedAtOnce()
    {
        var result = _validator.Validate(new ResetPasswordCommand("", "", ""));

        result.Errors.Select(failure => failure.PropertyName).Distinct(StringComparer.Ordinal)
            .ShouldBe(["Email", "Token", "NewPassword"], ignoreOrder: true);
    }

    private void ShouldFailOn(ResetPasswordCommand request, string propertyName)
    {
        var result = _validator.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == propertyName);
    }
}
