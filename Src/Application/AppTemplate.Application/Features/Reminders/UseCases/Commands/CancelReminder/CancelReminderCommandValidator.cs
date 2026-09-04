using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public sealed class CancelReminderCommandValidator : AbstractValidator<CancelReminderCommand>
{
    public CancelReminderCommandValidator() =>
        RuleFor(command => command.ReminderId)
            .NotEmpty().WithMessage("A reminder id is required.");
}
