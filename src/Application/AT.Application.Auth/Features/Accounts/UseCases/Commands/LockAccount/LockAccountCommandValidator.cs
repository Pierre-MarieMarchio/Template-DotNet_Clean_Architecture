using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.LockAccount;

public sealed class LockAccountCommandValidator : AbstractValidator<LockAccountCommand>
{
    public LockAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
