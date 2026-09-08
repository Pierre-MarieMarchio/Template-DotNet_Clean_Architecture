using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RefreshAccessToken;

/// <summary>Presence only: an unknown token and a malformed one must be answered identically.</summary>
public sealed class RefreshAccessTokenCommandValidator : AbstractValidator<RefreshAccessTokenCommand>
{
    /// <summary>The token is required.</summary>
    public RefreshAccessTokenCommandValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().WithMessage("Refresh token is required.");
}
