using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DisableAccountTwoFactor;

public sealed class DisableAccountTwoFactorCommandValidator : AbstractValidator<DisableAccountTwoFactorCommand>
{
    public DisableAccountTwoFactorCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
