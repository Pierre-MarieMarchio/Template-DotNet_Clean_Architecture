using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.Validators;

public sealed class ConfirmEmailRequestValidatorTests
{
    private readonly ConfirmEmailRequestValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new ConfirmEmailRequest("someone@example.com", "a-token")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("", "a-token")]
    [InlineData("   ", "a-token")]
    [InlineData("someone@example.com", "")]
    [InlineData("someone@example.com", "   ")]
    public void AnIncompleteRequest_IsRejected(string email, string token) =>
        _validator.Validate(new ConfirmEmailRequest(email, token)).IsValid.ShouldBeFalse();

    [Fact]
    public void BothMissingFields_AreReportedAtOnce()
    {
        var result = _validator.Validate(new ConfirmEmailRequest("", ""));

        result.Errors.Count.ShouldBe(2);
    }
}
