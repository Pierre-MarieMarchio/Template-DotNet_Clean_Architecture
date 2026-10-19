using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeyRegistration;

/// <summary>
/// The two ceremony fields are demanded present — an absent one is not a credential mismatch the
/// port should answer, it is no attempt at all. The password's presence is not this validator's to
/// ask, for the reason <c>DeleteMyAccountCommandValidator</c> gives: which evidence the account
/// demands is the use case's decision from the profile. The caps are denial-of-service bounds only,
/// for the reason <see cref="PasswordInputPolicy"/> gives.
/// </summary>
public sealed class CompletePasskeyRegistrationCommandValidator : AbstractValidator<CompletePasskeyRegistrationCommand>
{
    /// <summary>Presence for the ceremony's two halves; length bounds for the rest.</summary>
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

    /// <summary>
    /// A label an owner writes to recognise a key in a list, not a field anything parses; anything
    /// longer is a mistake or an attempt to make the store hold prose.
    /// </summary>
    internal const int NameMaximumLength = 128;
}
