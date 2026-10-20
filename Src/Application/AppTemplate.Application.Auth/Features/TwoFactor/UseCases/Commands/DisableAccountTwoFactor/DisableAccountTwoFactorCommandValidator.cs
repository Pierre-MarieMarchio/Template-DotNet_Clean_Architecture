using FluentValidation;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.DisableAccountTwoFactor;

public sealed class DisableAccountTwoFactorCommandValidator : AbstractValidator<DisableAccountTwoFactorCommand>
{
    public DisableAccountTwoFactorCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}
