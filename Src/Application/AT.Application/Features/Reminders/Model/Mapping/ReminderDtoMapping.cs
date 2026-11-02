using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Domain.Features.Reminders.Model.Entities;

namespace AppTemplate.Application.Features.Reminders.Model.Mapping;

internal static class ReminderDtoMapping
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
            reminder.NotifiedAt);
    }
}
