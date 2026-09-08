using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmail;

/// <summary>
/// Presence only, and deliberately no address format rule: a malformed address here must fail the
/// same way an unknown one does, and a validator that refused it first would say which was which.
/// </summary>
public sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    /// <summary>Both fields are required.</summary>
    public ConfirmEmailCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Confirmation token is required.");
    }
}
