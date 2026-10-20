using FluentValidation;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.VerifyTwoFactor;

public sealed class VerifyTwoFactorCommandValidator : AbstractValidator<VerifyTwoFactorCommand>
{
    public VerifyTwoFactorCommandValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty().WithMessage("Challenge token is required.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Code is required.");
    }
}
