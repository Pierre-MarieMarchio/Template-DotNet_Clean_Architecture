using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;

public sealed class CompletePasskeySignInCommandValidator : AbstractValidator<CompletePasskeySignInCommand>
{
    public CompletePasskeySignInCommandValidator()
    {
        RuleFor(x => x.Credential).NotEmpty().WithMessage("Credential is required.");
        RuleFor(x => x.AuthenticationState).NotEmpty().WithMessage("Authentication state is required.");
    }
}
