using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidatorTests
{
    private readonly ChangePasswordCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new ChangePasswordCommand("old password", "correct horse battery"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankCurrentPassword_IsRejected(string currentPassword) =>
        ShouldFailOn(new ChangePasswordCommand(currentPassword, "correct horse battery"), "CurrentPassword");

    /// <summary>The same floor every new password is held to — see <c>RegisterCommandValidatorTests</c>.</summary>
    [Fact]
    public void ANewPasswordBelowTheFloor_IsRejected() =>
        ShouldFailOn(
            new ChangePasswordCommand("old password", new string('a', PasswordRules.AbsoluteMinimumPasswordLength - 1)),
            "NewPassword");

    [Fact]
    public void EveryBrokenField_IsReportedAtOnce()
    {
        var result = _validator.Validate(new ChangePasswordCommand("", ""));

        result.Errors.Select(failure => failure.PropertyName).Distinct(StringComparer.Ordinal)
            .ShouldBe(["CurrentPassword", "NewPassword"], ignoreOrder: true);
    }

    private void ShouldFailOn(ChangePasswordCommand request, string propertyName)
    {
        var result = _validator.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == propertyName);
    }
}
