using AppTemplate.Application.Features.Auth.UseCases.Commands.RemoveRole;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.RemoveRole;

public sealed class RemoveRoleCommandValidatorTests
{
    private readonly RemoveRoleCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new RemoveRoleCommand(Guid.CreateVersion7(), "Admin")).IsValid.ShouldBeTrue();

    [Fact]
    public void AnEmptyUserId_IsRejected() =>
        _validator.Validate(new RemoveRoleCommand(Guid.Empty, "Admin")).IsValid.ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankRole_IsRejected(string role) =>
        _validator.Validate(new RemoveRoleCommand(Guid.CreateVersion7(), role)).IsValid.ShouldBeFalse();
}
