using AppTemplate.Application.Features.Auth.UseCases.Commands.Logout;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.Logout;

public sealed class LogoutCommandValidatorTests
{
    private readonly LogoutCommandValidator _validator = new();

    [Fact]
    public void APresentToken_IsAccepted() =>
        _validator.Validate(new LogoutCommand("an-opaque-secret")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankToken_IsRejected(string refreshToken) =>
        _validator.Validate(new LogoutCommand(refreshToken)).IsValid.ShouldBeFalse();
}
