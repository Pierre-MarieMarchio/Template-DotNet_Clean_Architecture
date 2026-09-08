using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.Logout;

/// <summary>Presence only: an unknown token and a malformed one must be answered identically.</summary>
public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    /// <summary>The token is required.</summary>
    public LogoutCommandValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.");
}
