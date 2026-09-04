using AppTemplate.Application.Features.Auth.UseCases.Commands;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.Validators;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Reset token is required.");
        RuleFor(x => x.NewPassword).Password();
    }
}
