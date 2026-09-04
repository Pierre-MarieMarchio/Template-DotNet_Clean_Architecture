using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.LockAccount;

public sealed class LockAccountCommandValidator : AbstractValidator<LockAccountCommand>
{
    public LockAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
