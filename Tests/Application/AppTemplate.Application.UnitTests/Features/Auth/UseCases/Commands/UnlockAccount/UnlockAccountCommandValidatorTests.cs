using AppTemplate.Application.Features.Auth.UseCases.Commands.UnlockAccount;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.UnlockAccount;

public sealed class UnlockAccountCommandValidatorTests
{
    private readonly UnlockAccountCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new UnlockAccountCommand(Guid.CreateVersion7())).IsValid.ShouldBeTrue();

    [Fact]
    public void AnEmptyUserId_IsRejected() =>
        _validator.Validate(new UnlockAccountCommand(Guid.Empty)).IsValid.ShouldBeFalse();
}
