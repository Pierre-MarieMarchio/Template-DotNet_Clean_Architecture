using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.VerifyTwoFactor;

/// <summary>
/// Presence only, for the same reason as <c>LoginCommandValidator</c>: this endpoint is reached
/// anonymously, so rejecting a malformed challenge token here rather than leaving it for the store to
/// refuse would tell a caller more about the token's shape than a wrong answer should.
/// </summary>
public sealed class VerifyTwoFactorCommandValidator : AbstractValidator<VerifyTwoFactorCommand>
{
    public VerifyTwoFactorCommandValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty().WithMessage("Challenge token is required.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Code is required.");
    }
}
