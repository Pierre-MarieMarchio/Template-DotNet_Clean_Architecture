using AppTemplate.Application.Features.Auth.Policies;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
        RuleFor(x => x.NewPassword).Password();
    }
}
