using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ResendConfirmationEmail;

public sealed class ResendConfirmationEmailCommandValidator : AbstractValidator<ResendConfirmationEmailCommand>
{
    public ResendConfirmationEmailCommandValidator() =>
        RuleFor(x => x.Email).NotEmpty().WithMessage("Email is required.");
}
