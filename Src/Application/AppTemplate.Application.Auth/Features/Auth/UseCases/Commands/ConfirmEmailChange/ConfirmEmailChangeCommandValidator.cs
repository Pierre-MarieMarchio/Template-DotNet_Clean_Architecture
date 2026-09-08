using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmEmailChange;

/// <summary>Presence only, on the same terms as <c>ConfirmEmailCommandValidator</c>.</summary>
public sealed class ConfirmEmailChangeCommandValidator : AbstractValidator<ConfirmEmailChangeCommand>
{
    /// <summary>Both fields are required.</summary>
    public ConfirmEmailChangeCommandValidator()
    {
        RuleFor(x => x.NewEmail).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Token is required.");
    }
}
