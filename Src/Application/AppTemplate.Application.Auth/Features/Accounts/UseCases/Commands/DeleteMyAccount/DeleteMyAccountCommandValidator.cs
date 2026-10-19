using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteMyAccount;

/// <summary>
/// Presence is not this validator's to ask: <b>which</b> evidence the account demands — a password,
/// a code, or neither — is the use case's decision from the profile, and a presence rule here would
/// refuse the one legitimate caller that has nothing to present. The caps are denial-of-service
/// bounds only, for the reason <see cref="PasswordInputPolicy"/> gives.
/// </summary>
public sealed class DeleteMyAccountCommandValidator : AbstractValidator<DeleteMyAccountCommand>
{
    /// <summary>Length bounds only; the matchers decide whether a presented value is the credential.</summary>
    public DeleteMyAccountCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .MaximumLength(PasswordInputPolicy.MaximumLength)
            .When(x => x.CurrentPassword is not null);

        RuleFor(x => x.TwoFactorCode)
            .MaximumLength(CodeMaximumLength)
            .When(x => x.TwoFactorCode is not null);
    }

    /// <summary>
    /// An authenticator code is six digits and a recovery code a few words of it; anything longer is
    /// not a code any matcher will recognise, and this bound keeps a caller from making the store
    /// hold one.
    /// </summary>
    internal const int CodeMaximumLength = 128;
}
