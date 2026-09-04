using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Events;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Tracking;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

/// <summary>
/// The three jobs EF's change tracker cannot do for aggregates it does not track: the identity map, the
/// note that a row is on its way out, and the drain that domain-event dispatch depends on.
/// </summary>
/// <remarks>
/// No database and no <c>DbContext</c> here. <c>FlushTo</c> needs EF's change tracker and is covered end
/// to end in the integration suite; everything below is the tracker's own bookkeeping, and the failures
/// it guards — an event delivered twice, or never — do not need a database to happen.
/// </remarks>
public sealed class ReminderTrackerTests
{
    private static readonly DateTimeOffset _scheduledAt = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
    private static readonly DateTimeOffset _dueAt = _scheduledAt.AddMinutes(5);
    private static readonly DateTimeOffset _firedAt = _dueAt.AddMinutes(1);

    private readonly ReminderMapper _mapper = new();

    // ---- The identity map ----------------------------------------------------------------------

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
    public void Find_ReturnsNothing_ForAnAggregateNobodyLoaded()
    {
        ATracker().Find(Guid.CreateVersion7()).ShouldBeNull();
    }

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

    // ---- Draining ------------------------------------------------------------------------------

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

    /// <summary>
    /// The fallback path in the repository: an aggregate reconstructed elsewhere is in no identity map,
    /// and marking it removed has to take it in. Before it did, the aggregate was never tracked, so its
    /// events were never drained and never published — silently.
    /// </summary>
    [Fact]
    public void MarkRemoved_TakesInAnAggregateThatWasNeverTracked()
    {
        var tracker = ATracker();
        var aggregate = AFiredReminder();

        tracker.MarkRemoved(aggregate, _mapper.ToNewRecord(aggregate));

        tracker.DrainDomainEvents().ShouldHaveSingleItem();
    }

    /// <summary>
    /// The ordinary path, where the delete follows a load. The row already in the identity map is the one
    /// EF is tracking; a removal must not swap it for a stub, or the flush would write onto an object the
    /// change tracker has never seen.
    /// </summary>
    [Fact]
    public void MarkRemoved_KeepsTheTrackedRow_WhenTheAggregateIsAlreadyKnown()
    {
        var tracker = ATracker();
        var aggregate = AScheduledReminder();
        var tracked = Track(tracker, aggregate);

        tracker.MarkRemoved(aggregate, new ReminderRecord { Id = aggregate.Id });

        tracker.FindRecord(aggregate.Id).ShouldBeSameAs(tracked);
    }

    // ---- Restoring after a failed save ---------------------------------------------------------

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
    public void Restore_RejectsNull()
    {
        Should.Throw<ArgumentNullException>(() => ATracker().Restore(null!));
    }

    // ---- Fixture -------------------------------------------------------------------------------

    private ReminderTracker ATracker() => new(_mapper);

    private static Reminder AScheduledReminder() => Reminder.Schedule(
        AReminderAggregate.OwnerId,
        AReminderAggregate.TodoListId,
        AReminderAggregate.TodoItemId,
        _dueAt,
        _scheduledAt);

    /// <summary>A reminder that has raised its one domain event, ready to be tracked.</summary>
    private static Reminder AFiredReminder()
    {
        var reminder = AScheduledReminder();
        reminder.TryClaim(_firedAt, TimeSpan.FromMinutes(1));
        reminder.MarkNotified(_firedAt);

        return reminder;
    }

    /// <summary>Registers an aggregate the way the repository does, and hands back its row.</summary>
    private ReminderRecord Track(ReminderTracker tracker, Reminder aggregate)
    {
        var record = _mapper.ToNewRecord(aggregate);
        tracker.Track(aggregate, record);

        return record;
    }
}
