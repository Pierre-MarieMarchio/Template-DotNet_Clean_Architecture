using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;

public sealed class CompletePasskeyRegistrationCommandValidator : AbstractValidator<CompletePasskeyRegistrationCommand>
{
    public CompletePasskeyRegistrationCommandValidator()
    {
        RuleFor(x => x.Credential).NotEmpty().WithMessage("Credential is required.");
        RuleFor(x => x.RegistrationState).NotEmpty().WithMessage("Registration state is required.");

        RuleFor(x => x.Name)
            .MaximumLength(NameMaximumLength)
            .When(x => x.Name is not null);

        RuleFor(x => x.CurrentPassword)
            .MaximumLength(PasswordInputPolicy.MaximumLength)
            .When(x => x.CurrentPassword is not null);
    }

    internal const int NameMaximumLength = 128;
}
