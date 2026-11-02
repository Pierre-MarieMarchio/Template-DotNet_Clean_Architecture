using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteAccount;

public sealed class DeleteAccountCommandValidator : AbstractValidator<DeleteAccountCommand>
{
    public DeleteAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
