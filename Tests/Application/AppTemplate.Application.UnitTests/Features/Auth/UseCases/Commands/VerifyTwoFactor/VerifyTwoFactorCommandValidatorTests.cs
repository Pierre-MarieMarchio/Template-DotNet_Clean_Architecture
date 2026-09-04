using AppTemplate.Application.Features.Auth.UseCases.Commands.VerifyTwoFactor;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.VerifyTwoFactor;

public sealed class VerifyTwoFactorCommandValidatorTests
{
    private readonly VerifyTwoFactorCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new VerifyTwoFactorCommand("a-challenge-token", "123456")).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("", "123456")]
    [InlineData("   ", "123456")]
    public void ABlankChallengeToken_IsRejected(string challengeToken, string code)
    {
        var result = _validator.Validate(new VerifyTwoFactorCommand(challengeToken, code));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "ChallengeToken");
    }

    [Theory]
    [InlineData("a-challenge-token", "")]
    [InlineData("a-challenge-token", "   ")]
    public void ABlankCode_IsRejected(string challengeToken, string code)
    {
        var result = _validator.Validate(new VerifyTwoFactorCommand(challengeToken, code));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "Code");
    }
}
