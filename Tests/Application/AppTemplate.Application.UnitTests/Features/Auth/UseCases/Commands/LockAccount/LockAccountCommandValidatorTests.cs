using AppTemplate.Application.Features.Auth.UseCases.Commands.LockAccount;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.LockAccount;

public sealed class LockAccountCommandValidatorTests
{
    private readonly LockAccountCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new LockAccountCommand(Guid.CreateVersion7())).IsValid.ShouldBeTrue();

    [Fact]
    public void AnEmptyUserId_IsRejected() =>
        _validator.Validate(new LockAccountCommand(Guid.Empty)).IsValid.ShouldBeFalse();
}
