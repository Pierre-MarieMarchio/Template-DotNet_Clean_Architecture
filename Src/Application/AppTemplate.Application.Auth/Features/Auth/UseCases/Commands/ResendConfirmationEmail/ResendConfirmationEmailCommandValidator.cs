using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResendConfirmationEmail;

/// <summary>
/// Presence only, and no format rule: this endpoint answers identically for every address, and a
/// validation failure on a malformed one would be a difference a caller could read.
/// </summary>
public sealed class ResendConfirmationEmailCommandValidator : AbstractValidator<ResendConfirmationEmailCommand>
{
    /// <summary>The address is required.</summary>
    public ResendConfirmationEmailCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
}
