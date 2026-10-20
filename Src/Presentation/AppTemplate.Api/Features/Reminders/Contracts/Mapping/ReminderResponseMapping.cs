using AppTemplate.Api.Features.Reminders.Contracts.Responses;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;

namespace AppTemplate.Api.Features.Reminders.Contracts.Mapping;

internal static class ReminderResponseMapping
{
    public static ReminderResponse ToResponse(ReminderDto reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        return new ReminderResponse(
            reminder.Id,
            reminder.TodoListId,
            reminder.TodoItemId,
            reminder.DueAt,
            ToStatus(reminder.State),
            reminder.NotifiedAt);
    }

    public static RemindersResponse ToResponse(IReadOnlyList<ReminderDto> reminders)
    {
        ArgumentNullException.ThrowIfNull(reminders);

        return new RemindersResponse([.. reminders.Select(ToResponse)]);
    }

    public static Result<Versioned<ReminderResponse>> ToReminderResponse(Result<Versioned<ReminderDto>> result) =>
        result.Map(value => new Versioned<ReminderResponse>(ToResponse(value.Value), value.Version));

    public static Result<RemindersResponse> ToRemindersResponse(Result<IReadOnlyList<ReminderDto>> result) =>
        result.Map(value => ToResponse(value));

    private static string ToStatus(ReminderState state) => state switch
    {
        ReminderState.Pending => "pending",
        ReminderState.Fired => "fired",
        ReminderState.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown reminder state."),
    };
}
