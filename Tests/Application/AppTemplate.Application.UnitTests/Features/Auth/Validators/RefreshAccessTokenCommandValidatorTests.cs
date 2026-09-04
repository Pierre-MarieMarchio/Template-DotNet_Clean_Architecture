using AppTemplate.Application.Features.Auth.UseCases.Commands;
using AppTemplate.Application.Features.Auth.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.Validators;

public sealed class RefreshAccessTokenCommandValidatorTests
{
    private readonly RefreshAccessTokenCommandValidator _validator = new();

    [Fact]
    public void APresentToken_IsAccepted() =>
        _validator.Validate(new RefreshAccessTokenCommand("an-opaque-secret")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankToken_IsRejected(string refreshToken) =>
        _validator.Validate(new RefreshAccessTokenCommand(refreshToken)).IsValid.ShouldBeFalse();
}
