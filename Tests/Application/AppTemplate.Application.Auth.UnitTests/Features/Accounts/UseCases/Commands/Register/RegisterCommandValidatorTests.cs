using AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.Register;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Accounts.UseCases.Commands.Register;

public sealed class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new RegisterCommand("someone@example.com", "correct horse battery"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    [InlineData("@example.com")]
    public void AMalformedEmail_IsRejected(string email) =>
        ShouldFailOn(new RegisterCommand(email, "correct horse battery"), "Email");

    [Fact]
    public void AnEmailBeyondTheMaximumLength_IsRejected() =>
        ShouldFailOn(
            new RegisterCommand($"{new string('a', 250)}@example.com", "correct horse battery"),
            "Email");

    [Fact]
    public void AShortPassword_IsLeftToTheStore() =>
        _validator.Validate(new RegisterCommand("someone@example.com", "a"))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void APasswordAtTheMaximumLength_IsAccepted() =>
        _validator.Validate(new RegisterCommand(
                "someone@example.com",
                new string('a', RegisterCommandValidator.MaximumPasswordLength)))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void APasswordBeyondTheMaximumLength_IsRejected() =>
        ShouldFailOn(
            new RegisterCommand(
                "someone@example.com",
                new string('a', RegisterCommandValidator.MaximumPasswordLength + 1)),
            "Password");

    [Fact]
    public void AnEmptyPassword_IsRejected() =>
        ShouldFailOn(new RegisterCommand("someone@example.com", ""), "Password");

    [Fact]
    public void ThePasswordCeiling_IsTwoHundredAndFiftySixCharacters()
    {
        RegisterCommandValidator.MaximumPasswordLength.ShouldBe(256);

        _validator.Validate(new RegisterCommand("someone@example.com", new string('a', 256)))
            .IsValid.ShouldBeTrue();
        _validator.Validate(new RegisterCommand("someone@example.com", new string('a', 257)))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void EveryBrokenField_IsReportedAtOnce()
    {
        var result = _validator.Validate(new RegisterCommand("", ""));

        result.Errors.Select(failure => failure.PropertyName).Distinct(StringComparer.Ordinal)
            .ShouldBe(["Email", "Password"], ignoreOrder: true);
    }

    private void ShouldFailOn(RegisterCommand request, string propertyName)
    {
        var result = _validator.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == propertyName);
    }
}
