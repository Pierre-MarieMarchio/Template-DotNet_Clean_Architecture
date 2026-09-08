using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.AddRole;

/// <summary>Presence only. Whether the role or the account exists is the store's answer.</summary>
public sealed class AddRoleCommandValidator : AbstractValidator<AddRoleCommand>
{
    /// <summary>Both fields are required; neither has a shape this layer could check further.</summary>
    public AddRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty();
    }
}
