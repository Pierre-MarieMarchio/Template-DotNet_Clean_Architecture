using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.UnlockAccount;

public sealed class UnlockAccountCommandValidator : AbstractValidator<UnlockAccountCommand>
{
    public UnlockAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
