using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

/// <summary>
/// Presence only. The exact shape of a code — six digits from an authenticator app — is
/// <c>ITwoFactorEnrollment</c>'s to enforce by simply not matching anything else; rejecting a
/// malformed value here would just be a second, less informative place that decision is made.
/// </summary>
public sealed class ConfirmTwoFactorSetupCommandValidator : AbstractValidator<ConfirmTwoFactorSetupCommand>
{
    public ConfirmTwoFactorSetupCommandValidator() =>
        RuleFor(x => x.Code).NotEmpty().WithMessage("Code is required.");
}
