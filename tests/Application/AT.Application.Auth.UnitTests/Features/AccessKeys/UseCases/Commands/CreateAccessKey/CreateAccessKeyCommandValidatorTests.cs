using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.CreateAccessKey;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.AccessKeys.UseCases.Commands.CreateAccessKey;

public sealed class CreateAccessKeyCommandValidatorTests
{
    private readonly CreateAccessKeyCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new CreateAccessKeyCommand("CI", ["read", "write"], null, "the password"))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void NoPasswordAtAll_IsAccepted() =>
        _validator.Validate(new CreateAccessKeyCommand("CI", ["read"], null, null)).IsValid.ShouldBeTrue();

    [Fact]
    public void ANameAtTheBound_IsAccepted() =>
        _validator.Validate(new CreateAccessKeyCommand(
            new string('x', AccessKeyPolicy.NameMaxLength), ["read"], null, null)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankName_IsRejected(string name) => ShouldRejectOn(
        new CreateAccessKeyCommand(name, ["read"], null, null), "Name");

    [Fact]
    public void ANameLongerThanTheBound_IsRejected() => ShouldRejectOn(
        new CreateAccessKeyCommand(new string('x', AccessKeyPolicy.NameMaxLength + 1), ["read"], null, null),
        "Name");

    [Fact]
    public void NoScope_IsRejected() => ShouldRejectOn(new CreateAccessKeyCommand("CI", [], null, null), "Scopes");

    [Fact]
    public void AScopeNamedTwice_IsRejected() => ShouldRejectOn(
        new CreateAccessKeyCommand("CI", ["read", "read"], null, null), "Scopes");

    [Theory]
    [InlineData("admin")]
    [InlineData("Read")]
    [InlineData("")]
    public void AScopeThePolicyDoesNotKnow_IsRejected(string scope) => ShouldRejectOn(
        new CreateAccessKeyCommand("CI", ["read", scope], null, null), "Scopes[1]");

    [Fact]
    public void APasswordLongerThanTheBound_IsRejected() => ShouldRejectOn(
        new CreateAccessKeyCommand("CI", ["read"], null, new string('x', PasswordInputPolicy.MaximumLength + 1)),
        "CurrentPassword");

    private void ShouldRejectOn(CreateAccessKeyCommand command, string propertyName)
    {
        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == propertyName);
    }
}
