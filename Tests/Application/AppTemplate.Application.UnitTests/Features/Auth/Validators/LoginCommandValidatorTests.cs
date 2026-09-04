using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.Validators;

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new LoginCommand("someone@example.com", "correct horse battery"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("", "correct horse battery")]
    [InlineData("   ", "correct horse battery")]
    [InlineData("someone@example.com", "")]
    [InlineData("someone@example.com", "   ")]
    public void AnIncompleteRequest_IsRejected(string email, string password) =>
        _validator.Validate(new LoginCommand(email, password)).IsValid.ShouldBeFalse();

    /// <summary>
    /// Login checks presence only. Rejecting a malformed address here would tell an attacker
    /// which addresses are even worth trying, and there is no length or format rule a
    /// credential comparison needs.
    /// </summary>
    [Fact]
    public void AMalformedEmail_IsAcceptedBecauseLoginChecksPresenceOnly() =>
        _validator.Validate(new LoginCommand("not-an-email", "correct horse battery")).IsValid.ShouldBeTrue();

    /// <summary>
    /// The password floor is deliberately not repeated here: an existing account may predate
    /// the current policy, and refusing its owner a login would lock them out.
    /// </summary>
    [Fact]
    public void AShortPassword_IsAcceptedBecauseAnExistingAccountMayPredateThePolicy() =>
        _validator.Validate(new LoginCommand("someone@example.com", "a")).IsValid.ShouldBeTrue();
}
