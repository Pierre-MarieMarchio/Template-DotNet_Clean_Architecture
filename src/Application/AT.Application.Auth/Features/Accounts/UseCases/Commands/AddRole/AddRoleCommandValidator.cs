using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.AddRole;

public sealed class AddRoleCommandValidator : AbstractValidator<AddRoleCommand>
{
    public AddRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Role).NotEmpty();
    }
}
