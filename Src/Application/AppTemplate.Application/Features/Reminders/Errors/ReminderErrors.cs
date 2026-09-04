using AppTemplate.Application.Common;

namespace AppTemplate.Application.Features.Reminders.Errors;

public static class ReminderErrors
{
    /// <summary>
    /// Also returned when the reminder belongs to somebody else: distinguishing the two would let
    /// a caller enumerate other users' reminder ids by comparing 403 against 404.
    /// </summary>
    public static Error ReminderNotFound(Guid reminderId) => Error.NotFound(
        "reminder.notFound",
        $"No reminder with id '{reminderId}' was found.");

    /// <summary>
    /// Scheduling refused because the named item does not exist, is not on the named list, or the
    /// list is not the caller's — the three are indistinguishable for the same reason
    /// <see cref="ReminderNotFound"/> hides ownership.
    /// </summary>
    public static Error TargetNotFound(Guid todoItemId) => Error.NotFound(
        "reminder.targetNotFound",
        $"No to-do item with id '{todoItemId}' was found.");
}
