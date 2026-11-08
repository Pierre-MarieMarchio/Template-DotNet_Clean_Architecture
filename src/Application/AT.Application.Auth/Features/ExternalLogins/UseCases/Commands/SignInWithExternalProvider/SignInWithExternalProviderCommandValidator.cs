using FluentValidation;

namespace AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;

public sealed class SignInWithExternalProviderCommandValidator
    : AbstractValidator<SignInWithExternalProviderCommand>
{
    public SignInWithExternalProviderCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty().WithMessage("Provider is required.");
        RuleFor(x => x.IdToken).NotEmpty().WithMessage("IdToken is required.");
    }
}
