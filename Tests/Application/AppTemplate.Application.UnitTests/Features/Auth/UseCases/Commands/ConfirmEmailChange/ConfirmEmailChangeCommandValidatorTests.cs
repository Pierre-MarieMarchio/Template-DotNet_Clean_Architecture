using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmailChange;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ConfirmEmailChange;

public sealed class ConfirmEmailChangeCommandValidatorTests
{
    private readonly ConfirmEmailChangeCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new ConfirmEmailChangeCommand("someone@example.com", "a-token")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("", "a-token")]
    [InlineData("   ", "a-token")]
    [InlineData("someone@example.com", "")]
    [InlineData("someone@example.com", "   ")]
    public void AnIncompleteRequest_IsRejected(string newEmail, string token) =>
        _validator.Validate(new ConfirmEmailChangeCommand(newEmail, token)).IsValid.ShouldBeFalse();
}
