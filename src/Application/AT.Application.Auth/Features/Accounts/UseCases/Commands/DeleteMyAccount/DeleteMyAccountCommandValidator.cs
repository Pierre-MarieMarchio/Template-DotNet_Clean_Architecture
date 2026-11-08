using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;

public sealed class DeleteMyAccountCommandValidator : AbstractValidator<DeleteMyAccountCommand>
{
    public DeleteMyAccountCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .MaximumLength(PasswordInputPolicy.MaximumLength)
            .When(x => x.CurrentPassword is not null);

        RuleFor(x => x.TwoFactorCode)
            .MaximumLength(CodeMaximumLength)
            .When(x => x.TwoFactorCode is not null);
    }

    internal const int CodeMaximumLength = 128;
}
