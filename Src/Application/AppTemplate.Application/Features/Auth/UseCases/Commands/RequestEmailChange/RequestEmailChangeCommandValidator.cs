using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RequestEmailChange;

/// <summary>
/// Shape only, mirroring <c>RegisterCommandValidator</c>'s email rule. The current-password check
/// itself is the store's, not this validator's.
/// </summary>
public sealed class RequestEmailChangeCommandValidator : AbstractValidator<RequestEmailChangeCommand>
{
    public RequestEmailChangeCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewEmail)
            .NotEmpty().WithMessage("Email is required.")
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.")
            .EmailAddress().WithMessage("Invalid email format.");
    }
}
