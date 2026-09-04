using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.Validators;

public sealed class LogoutRequestValidatorTests
{
    private readonly LogoutRequestValidator _validator = new();

    [Fact]
    public void APresentToken_IsAccepted() =>
        _validator.Validate(new LogoutRequest("an-opaque-secret")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankToken_IsRejected(string refreshToken) =>
        _validator.Validate(new LogoutRequest(refreshToken)).IsValid.ShouldBeFalse();
}
