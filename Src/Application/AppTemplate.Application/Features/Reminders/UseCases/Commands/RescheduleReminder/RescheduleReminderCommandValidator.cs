using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;

public sealed class RescheduleReminderCommandValidator : AbstractValidator<RescheduleReminderCommand>
{
    public RescheduleReminderCommandValidator() =>
        RuleFor(command => command.ReminderId)
            .NotEmpty().WithMessage("A reminder id is required.");
}
