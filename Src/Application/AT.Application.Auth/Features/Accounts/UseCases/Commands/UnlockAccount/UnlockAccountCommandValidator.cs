using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.UnlockAccount;

public sealed class UnlockAccountCommandValidator : AbstractValidator<UnlockAccountCommand>
{
    public UnlockAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
