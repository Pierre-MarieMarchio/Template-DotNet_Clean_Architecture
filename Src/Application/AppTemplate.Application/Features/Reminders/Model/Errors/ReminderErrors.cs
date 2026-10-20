using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.Reminders.Model.Errors;

public static class ReminderErrors
{
    public static Error ReminderNotFound(Guid reminderId) => Error.NotFound(
        "reminder.notFound",
        $"No reminder with id '{reminderId}' was found.");

    public static Error TargetNotFound(Guid todoItemId) => Error.NotFound(
        "reminder.targetNotFound",
        $"No to-do item with id '{todoItemId}' was found.");
}
