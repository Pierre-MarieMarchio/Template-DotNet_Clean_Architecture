using AppTemplate.Application.Features.Auth.UseCases.Commands;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.Validators;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required.");
        RuleFor(x => x.NewPassword).Password();
    }
}
