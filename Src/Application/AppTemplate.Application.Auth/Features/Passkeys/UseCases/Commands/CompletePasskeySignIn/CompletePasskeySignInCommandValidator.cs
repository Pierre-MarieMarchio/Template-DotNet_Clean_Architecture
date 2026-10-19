using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.CompletePasskeySignIn;

/// <summary>
/// Presence only, for the same reason as <c>VerifyTwoFactorCommandValidator</c>: this endpoint is
/// reached anonymously, so rejecting a malformed state here rather than leaving it for the
/// ceremony to refuse would tell a caller more about the state's shape than a wrong answer should.
/// </summary>
public sealed class CompletePasskeySignInCommandValidator : AbstractValidator<CompletePasskeySignInCommand>
{
    /// <summary>Both fields are required, and neither is inspected any further.</summary>
    public CompletePasskeySignInCommandValidator()
    {
        RuleFor(x => x.Credential).NotEmpty().WithMessage("Credential is required.");
        RuleFor(x => x.AuthenticationState).NotEmpty().WithMessage("Authentication state is required.");
    }
}
