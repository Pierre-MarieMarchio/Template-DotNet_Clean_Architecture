using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.Validators;

/// <summary>
/// A validator that accepts everything and one that is never consulted look identical from the
/// use case, so both halves are pinned: what is rejected, and what must still be accepted.
/// </summary>
public sealed class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new RegisterCommand("someone", "someone@example.com", "correct horse battery"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankUserName_IsRejected(string userName) =>
        ShouldFailOn(new RegisterCommand(userName, "someone@example.com", "correct horse battery"), "UserName");

    [Fact]
    public void AUserNameAtTheMaximumLength_IsAccepted() =>
        _validator.Validate(new RegisterCommand(
                new string('a', RegisterCommandValidator.MaximumUserNameLength),
                "someone@example.com",
                "correct horse battery"))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void AUserNameBeyondTheMaximumLength_IsRejected() =>
        ShouldFailOn(
            new RegisterCommand(
                new string('a', RegisterCommandValidator.MaximumUserNameLength + 1),
                "someone@example.com",
                "correct horse battery"),
            "UserName");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    [InlineData("@example.com")]
    public void AMalformedEmail_IsRejected(string email) =>
        ShouldFailOn(new RegisterCommand("someone", email, "correct horse battery"), "Email");

    [Fact]
    public void AnEmailBeyondTheMaximumLength_IsRejected() =>
        ShouldFailOn(
            new RegisterCommand("someone", $"{new string('a', 250)}@example.com", "correct horse battery"),
            "Email");

    /// <summary>
    /// The floor mirrors the hard minimum the Identity configuration cannot go below, so
    /// lowering it here would let a password through that the user store then refuses.
    /// </summary>
    [Fact]
    public void APasswordOneCharacterBelowTheFloor_IsRejected() =>
        ShouldFailOn(
            new RegisterCommand(
                "someone",
                "someone@example.com",
                new string('a', RegisterCommandValidator.AbsoluteMinimumPasswordLength - 1)),
            "Password");

    [Fact]
    public void APasswordExactlyAtTheFloor_IsAccepted() =>
        _validator.Validate(new RegisterCommand(
                "someone",
                "someone@example.com",
                new string('a', RegisterCommandValidator.AbsoluteMinimumPasswordLength)))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void APasswordAtTheMaximumLength_IsAccepted() =>
        _validator.Validate(new RegisterCommand(
                "someone",
                "someone@example.com",
                new string('a', RegisterCommandValidator.MaximumPasswordLength)))
            .IsValid.ShouldBeTrue();

    /// <summary>Guards against a denial of service through an arbitrarily long PBKDF2 input.</summary>
    [Fact]
    public void APasswordBeyondTheMaximumLength_IsRejected() =>
        ShouldFailOn(
            new RegisterCommand(
                "someone",
                "someone@example.com",
                new string('a', RegisterCommandValidator.MaximumPasswordLength + 1)),
            "Password");

    [Fact]
    public void AnEmptyPassword_IsRejected() =>
        ShouldFailOn(new RegisterCommand("someone", "someone@example.com", ""), "Password");

    /// <summary>
    /// Asserted against a literal on purpose: every other password case here derives its input from
    /// <see cref="RegisterCommandValidator.AbsoluteMinimumPasswordLength"/> and would move with the
    /// constant, so lowering it would go unnoticed. Eight is what
    /// <c>IdentityPolicyOptions.AbsoluteMinimumPasswordLength</c> clamps the configured policy to,
    /// and this layer cannot reference that assembly — this is the only place the mirror is held.
    /// </summary>
    [Fact]
    public void ThePasswordFloor_IsEightCharacters()
    {
        RegisterCommandValidator.AbsoluteMinimumPasswordLength.ShouldBe(8);

        _validator.Validate(new RegisterCommand("someone", "someone@example.com", "sevench"))
            .IsValid.ShouldBeFalse();
        _validator.Validate(new RegisterCommand("someone", "someone@example.com", "eightchr"))
            .IsValid.ShouldBeTrue();
    }

    /// <summary>
    /// Asserted against a literal for the same reason, and it matters more here: the ceiling exists
    /// so one request cannot turn into an unbounded amount of PBKDF2 work.
    /// </summary>
    [Fact]
    public void ThePasswordCeiling_IsTwoHundredAndFiftySixCharacters()
    {
        RegisterCommandValidator.MaximumPasswordLength.ShouldBe(256);

        _validator.Validate(new RegisterCommand("someone", "someone@example.com", new string('a', 256)))
            .IsValid.ShouldBeTrue();
        _validator.Validate(new RegisterCommand("someone", "someone@example.com", new string('a', 257)))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void EveryBrokenField_IsReportedAtOnce()
    {
        var result = _validator.Validate(new RegisterCommand("", "", ""));

        result.Errors.Select(failure => failure.PropertyName).Distinct(StringComparer.Ordinal)
            .ShouldBe(["UserName", "Email", "Password"], ignoreOrder: true);
    }

    private void ShouldFailOn(RegisterCommand request, string propertyName)
    {
        var result = _validator.Validate(request);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == propertyName);
    }
}
