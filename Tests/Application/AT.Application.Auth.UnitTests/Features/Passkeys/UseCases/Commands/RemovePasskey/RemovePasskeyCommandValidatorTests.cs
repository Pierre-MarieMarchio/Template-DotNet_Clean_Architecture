using AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Auth.UnitTests.Features.Passkeys.UseCases.Commands.RemovePasskey;

public sealed class RemovePasskeyCommandValidatorTests
{
    private readonly RemovePasskeyCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new RemovePasskeyCommand([1, 2, 3])).IsValid.ShouldBeTrue();

    [Fact]
    public void AnEmptyCredentialId_IsRejected()
    {
        var result = _validator.Validate(new RemovePasskeyCommand([]));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(failure => failure.PropertyName == "CredentialId");
    }
}
