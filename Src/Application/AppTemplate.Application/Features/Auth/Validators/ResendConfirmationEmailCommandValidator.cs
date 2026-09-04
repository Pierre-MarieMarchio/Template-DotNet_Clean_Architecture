using AppTemplate.Application.Features.Auth.UseCases.Commands;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.Validators;

public sealed class ResendConfirmationEmailCommandValidator : AbstractValidator<ResendConfirmationEmailCommand>
{
    public ResendConfirmationEmailCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
}
