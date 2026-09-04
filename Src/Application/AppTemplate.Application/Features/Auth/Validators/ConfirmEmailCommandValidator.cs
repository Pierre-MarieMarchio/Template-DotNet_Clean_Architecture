using AppTemplate.Application.Features.Auth.UseCases.Commands;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.Validators;

public sealed class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Confirmation token is required.");
    }
}
