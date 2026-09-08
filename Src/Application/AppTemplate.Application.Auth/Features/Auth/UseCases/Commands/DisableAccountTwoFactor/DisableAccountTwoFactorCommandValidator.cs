using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DisableAccountTwoFactor;

/// <summary>An empty id is refused here so it never reaches the store as a lookup that misses.</summary>
public sealed class DisableAccountTwoFactorCommandValidator : AbstractValidator<DisableAccountTwoFactorCommand>
{
    /// <summary>The target id is required.</summary>
    public DisableAccountTwoFactorCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
