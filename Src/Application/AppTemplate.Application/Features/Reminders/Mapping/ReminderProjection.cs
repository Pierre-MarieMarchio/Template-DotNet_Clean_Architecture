using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Domain.Features.Reminders.Entities;

namespace AppTemplate.Application.Features.Reminders.Mapping;

/// <summary>
/// Turns the aggregate a command just wrote into the same shape a read would have produced,
/// without a second query — the same reasoning as <c>TodoListProjection</c>, for the same
/// aggregate-is-flat reason <see cref="Reminder"/>'s own doc comment gives.
/// </summary>
internal static class ReminderProjection
{
    public static Versioned<ReminderDto> ToVersioned(Reminder reminder) => new(ToDto(reminder), reminder.Version);

    public static ReminderDto ToDto(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        return new ReminderDto(
            reminder.Id,
            reminder.TodoListId,
            reminder.TodoItemId,
            reminder.DueAt,
            reminder.State,
            reminder.ClaimedAt,
            reminder.NotifiedAt);
    }
}
