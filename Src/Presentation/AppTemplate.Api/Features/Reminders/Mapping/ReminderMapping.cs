using AppTemplate.Api.Features.Reminders.Contracts.Responses;
using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Api.Features.Reminders.Mapping;

/// <summary>
/// Projects the feature's application DTO onto its wire contracts, by hand — see
/// <c>TodoListMapping</c> for why: positional records plus <c>TreatWarningsAsErrors</c> make a
/// member added on either side fail the build here, where a convention-based mapper would have
/// turned the forgotten field into a naming rule nobody reads.
/// </summary>
internal static class ReminderMapping
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

    public static Result<Versioned<ReminderResponse>> ToReminderResponse(Result<Versioned<ReminderDto>> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<Versioned<ReminderResponse>>();
        }

        return new Versioned<ReminderResponse>(ToResponse(result.Value.Value), result.Value.Version);
    }

    public static Result<RemindersResponse> ToRemindersResponse(Result<IReadOnlyList<ReminderDto>> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<RemindersResponse>();
        }

        return ToResponse(result.Value);
    }

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
