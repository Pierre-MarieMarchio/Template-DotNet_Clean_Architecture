using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DeleteAccount;

public sealed class DeleteAccountCommandValidator : AbstractValidator<DeleteAccountCommand>
{
    public DeleteAccountCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
