using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RemoveRole;

/// <summary>Presence only. Whether the account holds the role is the store's answer.</summary>
public sealed class RemoveRoleCommandValidator : AbstractValidator<RemoveRoleCommand>
{
    /// <summary>Both fields are required; neither has a shape this layer could check further.</summary>
    public RemoveRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty();
    }
}
