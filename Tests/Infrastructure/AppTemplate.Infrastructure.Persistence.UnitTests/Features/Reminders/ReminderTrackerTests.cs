using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Model.Events;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Tracking;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

public sealed class ReminderTrackerTests
{
    private static readonly DateTimeOffset _scheduledAt = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
    private static readonly DateTimeOffset _dueAt = _scheduledAt.AddMinutes(5);
    private static readonly DateTimeOffset _firedAt = _dueAt.AddMinutes(1);

    private readonly ReminderMapper _mapper = new();

    [Fact]
    public void Find_ReturnsTheVerySameInstanceEveryTime()
    {
        var tracker = ATracker();
        var aggregate = AScheduledReminder();

        Track(tracker, aggregate);

        var first = tracker.Find(aggregate.Id);
        var second = tracker.Find(aggregate.Id);

        first.ShouldBeSameAs(aggregate);
        second.ShouldBeSameAs(
            aggregate,
            "two callers in one request holding different copies would each decide against their own, "
            + "and the flush would keep whichever it saw last.");
    }

    [Fact]
    public void Find_ReturnsNothing_ForAnAggregateNobodyLoaded() =>
        ATracker().Find(Guid.CreateVersion7()).ShouldBeNull();

    [Fact]
    public void Find_StopsReturningARemovedAggregate_ButItsRowIsStillThere()
    {
        var tracker = ATracker();
        var aggregate = AScheduledReminder();
        var record = Track(tracker, aggregate);

        tracker.MarkRemoved(aggregate, record);

        tracker.Find(aggregate.Id).ShouldBeNull("a deleted aggregate must not be handed out again");
        tracker.FindRecord(aggregate.Id).ShouldBeSameAs(
            record,
            "the row is still staged for deletion, and the repository needs it to attach the token.");
    }

    [Fact]
    public void DrainDomainEvents_YieldsEachEventExactlyOnce()
    {
        var tracker = ATracker();
        var aggregate = AFiredReminder();
        Track(tracker, aggregate);

        var drained = tracker.DrainDomainEvents();

        drained.ShouldHaveSingleItem().ShouldBeOfType<ReminderFiredDomainEvent>();
        tracker.DrainDomainEvents().ShouldBeEmpty(
            "an event that was taken cannot be taken again, or a second save in the same request would "
            + "publish everything the first one did.");
    }

    [Fact]
    public void DrainDomainEvents_CollectsFromEveryTrackedAggregate()
    {
        var tracker = ATracker();
        var first = AFiredReminder();
        var second = AFiredReminder();

        Track(tracker, first);
        Track(tracker, second);

        tracker.DrainDomainEvents()
            .OfType<ReminderFiredDomainEvent>()
            .Select(raised => raised.ReminderId)
            .ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public void DrainDomainEvents_StillYieldsTheEventsOfARemovedAggregate()
    {
        var tracker = ATracker();
        var aggregate = AFiredReminder();
        var record = Track(tracker, aggregate);

        tracker.MarkRemoved(aggregate, record);

        tracker.DrainDomainEvents().ShouldHaveSingleItem(
            "a deletion is something that happened, and an event raised on the way out would otherwise "
            + "be undeliverable.");
    }

    [Fact]
    public void MarkRemoved_TakesInAnAggregateThatWasNeverTracked()
    {
        var tracker = ATracker();
        var aggregate = AFiredReminder();

        tracker.MarkRemoved(aggregate, _mapper.ToNewRecord(aggregate));

        tracker.DrainDomainEvents().ShouldHaveSingleItem();
    }

    [Fact]
    public void MarkRemoved_KeepsTheTrackedRow_WhenTheAggregateIsAlreadyKnown()
    {
        var tracker = ATracker();
        var aggregate = AScheduledReminder();
        var tracked = Track(tracker, aggregate);

        tracker.MarkRemoved(aggregate, new ReminderRecord { Id = aggregate.Id });

        tracker.FindRecord(aggregate.Id).ShouldBeSameAs(tracked);
    }

    [Fact]
    public void Restore_HandsTheEventsBackOnTheNextDrain()
    {
        var tracker = ATracker();
        var aggregate = AFiredReminder();
        Track(tracker, aggregate);

        var drained = tracker.DrainDomainEvents();
        tracker.Restore(drained);

        tracker.DrainDomainEvents().ShouldBe(drained);
        tracker.DrainDomainEvents().ShouldBeEmpty("restored events are drained once, like any other");
    }

    [Fact]
    public void Restore_PutsTheOlderEventsAheadOfAnythingRaisedSince()
    {
        var tracker = ATracker();
        var first = AFiredReminder();
        Track(tracker, first);

        var drained = tracker.DrainDomainEvents();
        drained.ShouldHaveSingleItem();

        var second = AFiredReminder();
        Track(tracker, second);
        tracker.Restore(drained);

        tracker.DrainDomainEvents()
            .OfType<ReminderFiredDomainEvent>()
            .Select(raised => raised.ReminderId)
            .ShouldBe([first.Id, second.Id]);
    }

    [Fact]
    public void Restore_RejectsNull() => Should.Throw<ArgumentNullException>(() => ATracker().Restore(null!));

    private ReminderTracker ATracker() => new(_mapper);

    private static Reminder AScheduledReminder() => Reminder.Schedule(
        AReminderAggregate.OwnerId,
        AReminderAggregate.TodoListId,
        AReminderAggregate.TodoItemId,
        _dueAt,
        _scheduledAt);

    private static Reminder AFiredReminder()
    {
        var reminder = AScheduledReminder();
        reminder.MarkNotified(_firedAt);

        return reminder;
    }

    private ReminderRecord Track(ReminderTracker tracker, Reminder aggregate)
    {
        var record = _mapper.ToNewRecord(aggregate);
        tracker.Track(aggregate, record);

        return record;
    }
}
