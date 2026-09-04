using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;

/// <summary>
/// Presence only, for the reason <c>LoginCommandValidator</c> gives. Neither field has a shape worth
/// asserting here: which provider names exist is an operator's configuration, and a token that is not
/// a well-formed JWT is a token the verifier refuses — rejecting it as a malformed request instead
/// would answer "is this provider configured?" and "is this token even the right kind?" for a caller
/// holding neither.
/// </summary>
public sealed class SignInWithExternalProviderCommandValidator
    : AbstractValidator<SignInWithExternalProviderCommand>
{
    public SignInWithExternalProviderCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty().WithMessage("Provider is required.");
        RuleFor(x => x.IdToken).NotEmpty().WithMessage("IdToken is required.");
    }
}
