using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

/// <summary>
/// Presence only for both fields. The exact shape of a code — six digits from an authenticator app —
/// is <c>ITwoFactorEnrollmentService</c>'s to enforce by simply not matching anything else; rejecting a
/// malformed value here would just be a second, less informative place that decision is made.
/// Whether the password is actually <em>right</em> is the same story, one layer over: the hasher is
/// what knows that, not a validator.
/// </summary>
public sealed class ConfirmTwoFactorSetupCommandValidator : AbstractValidator<ConfirmTwoFactorSetupCommand>
{
    public ConfirmTwoFactorSetupCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
        RuleFor(x => x.Code).NotEmpty().WithMessage("Code is required.");
    }
}
