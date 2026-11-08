using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;

public sealed class ScheduleReminderCommandValidator : AbstractValidator<ScheduleReminderCommand>
{
    public ScheduleReminderCommandValidator()
    {
        RuleFor(command => command.TodoListId)
            .NotEmpty().WithMessage("A list id is required.");

        RuleFor(command => command.TodoItemId)
            .NotEmpty().WithMessage("An item id is required.");
    }
}
