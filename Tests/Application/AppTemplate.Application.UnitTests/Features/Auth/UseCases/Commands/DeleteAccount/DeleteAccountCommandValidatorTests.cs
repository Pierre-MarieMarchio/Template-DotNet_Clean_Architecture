using AppTemplate.Application.Features.Auth.UseCases.Commands.DeleteAccount;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.DeleteAccount;

public sealed class DeleteAccountCommandValidatorTests
{
    private readonly DeleteAccountCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new DeleteAccountCommand(Guid.CreateVersion7())).IsValid.ShouldBeTrue();

    [Fact]
    public void AnEmptyUserId_IsRejected() =>
        _validator.Validate(new DeleteAccountCommand(Guid.Empty)).IsValid.ShouldBeFalse();
}
