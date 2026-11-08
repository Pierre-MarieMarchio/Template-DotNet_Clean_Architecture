using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Sessions.UseCases.Commands.Login;

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

    [Fact]
    public void AMalformedEmail_IsAcceptedBecauseLoginChecksPresenceOnly() =>
        _validator.Validate(new LoginCommand("not-an-email", "correct horse battery")).IsValid.ShouldBeTrue();

    [Fact]
    public void AShortPassword_IsAcceptedBecauseAnExistingAccountMayPredateThePolicy() =>
        _validator.Validate(new LoginCommand("someone@example.com", "a")).IsValid.ShouldBeTrue();
}
