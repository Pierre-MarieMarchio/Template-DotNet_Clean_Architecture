using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmEmail;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.ConfirmEmail;

public sealed class ConfirmEmailCommandValidatorTests
{
    private readonly ConfirmEmailCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new ConfirmEmailCommand("someone@example.com", "a-token")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("", "a-token")]
    [InlineData("   ", "a-token")]
    [InlineData("someone@example.com", "")]
    [InlineData("someone@example.com", "   ")]
    public void AnIncompleteRequest_IsRejected(string email, string token) =>
        _validator.Validate(new ConfirmEmailCommand(email, token)).IsValid.ShouldBeFalse();

    [Fact]
    public void BothMissingFields_AreReportedAtOnce()
    {
        var result = _validator.Validate(new ConfirmEmailCommand("", ""));

        result.Errors.Count.ShouldBe(2);
    }
}
