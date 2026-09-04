using AppTemplate.Application.Features.Auth.UseCases.Commands.AddRole;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Auth.UseCases.Commands.AddRole;

public sealed class AddRoleCommandValidatorTests
{
    private readonly AddRoleCommandValidator _validator = new();

    [Fact]
    public void AWellFormedRequest_IsAccepted() =>
        _validator.Validate(new AddRoleCommand(Guid.CreateVersion7(), "Admin")).IsValid.ShouldBeTrue();

    [Fact]
    public void AnEmptyUserId_IsRejected() =>
        _validator.Validate(new AddRoleCommand(Guid.Empty, "Admin")).IsValid.ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankRole_IsRejected(string role) =>
        _validator.Validate(new AddRoleCommand(Guid.CreateVersion7(), role)).IsValid.ShouldBeFalse();
}
