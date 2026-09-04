using AppTemplate.Api.Features.Reminders.Contracts.Responses;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Api.Features.Reminders.Mapping;

/// <summary>
/// Projects the feature's application DTO onto its wire contracts, by hand — see
/// <c>TodoListResponseMapping</c> for why: positional records plus <c>TreatWarningsAsErrors</c> make a
/// member added on either side fail the build here, where a convention-based mapper would have
/// turned the forgotten field into a naming rule nobody reads.
/// </summary>
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
            reminder.ClaimedAt,
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

    /// <summary>
    /// A string on the wire rather than the domain enum's own numeric default: the API's contract
    /// is its own, not a view of <see cref="ReminderState"/>, and a caller should never have to
    /// know the enum's declaration order to read a status.
    /// </summary>
    private static string ToStatus(ReminderState state) => state switch
    {
        ReminderState.Pending => "pending",
        ReminderState.Fired => "fired",
        ReminderState.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown reminder state."),
    };
}
