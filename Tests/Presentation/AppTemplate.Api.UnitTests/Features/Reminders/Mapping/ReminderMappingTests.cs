using AppTemplate.Api.Features.Reminders.Mapping;
using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Mapping;

/// <summary>
/// A hand-written mapper's only failure mode is a field nobody copied, so every test here asserts
/// on the whole shape rather than on the members that happen to be interesting.
/// </summary>
public sealed class ReminderMappingTests
{
    [Fact]
    public void ToResponse_Reminder_CopiesEveryField()
    {
        var reminder = new ReminderDto(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DueAt: new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            State: ReminderState.Fired,
            ClaimedAt: new DateTimeOffset(2026, 3, 4, 5, 5, 0, TimeSpan.Zero),
            NotifiedAt: new DateTimeOffset(2026, 3, 4, 5, 6, 1, TimeSpan.Zero));

        var response = ReminderMapping.ToResponse(reminder);

        response.Id.ShouldBe(reminder.Id);
        response.TodoListId.ShouldBe(reminder.TodoListId);
        response.TodoItemId.ShouldBe(reminder.TodoItemId);
        response.DueAt.ShouldBe(reminder.DueAt);
        response.Status.ShouldBe("fired");
        response.ClaimedAt.ShouldBe(reminder.ClaimedAt);
        response.NotifiedAt.ShouldBe(reminder.NotifiedAt);
    }

    [Fact]
    public void ToResponse_Reminder_CarriesTheAbsentInstants_AsAbsent()
    {
        var reminder = APendingReminder();

        var response = ReminderMapping.ToResponse(reminder);

        response.ClaimedAt.ShouldBeNull();
        response.NotifiedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(ReminderState.Pending, "pending")]
    [InlineData(ReminderState.Fired, "fired")]
    [InlineData(ReminderState.Cancelled, "cancelled")]
    public void ToResponse_Reminder_MapsEveryState_ToItsWireStatus(ReminderState state, string status)
    {
        var reminder = APendingReminder() with { State = state };

        var response = ReminderMapping.ToResponse(reminder);

        response.Status.ShouldBe(status);
    }

    [Fact]
    public void ToResponse_Reminders_KeepsTheReceivedOrder()
    {
        IReadOnlyList<ReminderDto> reminders = [APendingReminder(), APendingReminder(), APendingReminder()];

        var response = ReminderMapping.ToResponse(reminders);

        response.Reminders.Select(reminder => reminder.Id).ShouldBe([.. reminders.Select(reminder => reminder.Id)]);
    }

    /// <summary>
    /// A reminder-less item is still an envelope carrying an empty array, not an absent one: a
    /// client reading <c>reminders</c> must never have to distinguish null from empty.
    /// </summary>
    [Fact]
    public void ToResponse_Reminders_WrapsAnEmptyCollection_InAnEmptyArray()
    {
        var response = ReminderMapping.ToResponse((IReadOnlyList<ReminderDto>)[]);

        response.Reminders.ShouldNotBeNull();
        response.Reminders.ShouldBeEmpty();
    }

    [Fact]
    public void ToReminderResponse_KeepsTheVersion()
    {
        var reminder = APendingReminder();

        var result = ReminderMapping.ToReminderResponse(Result.Success(new Versioned<ReminderDto>(reminder, 7)));

        result.Value.Version.ShouldBe(7u);
        result.Value.Value.Id.ShouldBe(reminder.Id);
    }

    [Fact]
    public void ToReminderResponse_PropagatesAFailure_WithoutThrowing()
    {
        var result = ReminderMapping.ToReminderResponse(Result.Failure<Versioned<ReminderDto>>(_someError));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someError);
    }

    [Fact]
    public void ToRemindersResponse_CarriesEveryReminder()
    {
        IReadOnlyList<ReminderDto> reminders = [APendingReminder()];

        var result = ReminderMapping.ToRemindersResponse(Result.Success(reminders));

        result.Value.Reminders.Single().Id.ShouldBe(reminders[0].Id);
    }

    [Fact]
    public void ToRemindersResponse_PropagatesAFailure_WithoutThrowing()
    {
        var result = ReminderMapping.ToRemindersResponse(Result.Failure<IReadOnlyList<ReminderDto>>(_someError));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someError);
    }

    private static readonly Error _someError = Error.NotFound("reminder.notFound", "gone");

    private static ReminderDto APendingReminder() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DateTimeOffset.UnixEpoch.AddDays(1),
            ReminderState.Pending,
            null,
            null);
}
