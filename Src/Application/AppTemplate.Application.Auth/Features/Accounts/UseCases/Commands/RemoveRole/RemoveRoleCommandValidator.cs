using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RemoveRole;

public sealed class RemoveRoleCommandValidator : AbstractValidator<RemoveRoleCommand>
{
    public RemoveRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty();
    }
}
