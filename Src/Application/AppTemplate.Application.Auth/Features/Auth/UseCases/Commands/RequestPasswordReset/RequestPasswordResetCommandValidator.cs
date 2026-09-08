using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RequestPasswordReset;

/// <summary>
/// Presence only, and no format rule: a malformed address has to be answered exactly as an
/// unknown one is, and a validation failure would set the two apart.
/// </summary>
public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    /// <summary>The address is required.</summary>
    public RequestPasswordResetCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
}
