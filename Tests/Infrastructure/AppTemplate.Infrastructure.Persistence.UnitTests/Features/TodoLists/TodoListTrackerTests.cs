using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Tracking;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

public sealed class TodoListTrackerTests
{
    private static readonly DateTimeOffset _now = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);

    private readonly TodoListMapper _mapper = new();

    [Fact]
    public void Find_ReturnsTheVerySameInstanceEveryTime()
    {
        var tracker = ATracker();
        var aggregate = ANewList("Groceries");

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
        var aggregate = ANewList("Doomed");
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
        var aggregate = ANewList("Groceries");
        Track(tracker, aggregate);

        var drained = tracker.DrainDomainEvents();

        drained.ShouldHaveSingleItem().ShouldBeOfType<TodoListCreatedDomainEvent>();
        tracker.DrainDomainEvents().ShouldBeEmpty(
            "an event that was taken cannot be taken again, or a second save in the same request would "
            + "publish everything the first one did.");
    }

    [Fact]
    public void DrainDomainEvents_CollectsFromEveryTrackedAggregate()
    {
        var tracker = ATracker();
        var first = ANewList("First");
        var second = ANewList("Second");

        Track(tracker, first);
        Track(tracker, second);

        tracker.DrainDomainEvents()
            .OfType<TodoListCreatedDomainEvent>()
            .Select(raised => raised.TodoListId)
            .ShouldBe([first.Id, second.Id], ignoreOrder: true);
    }

    [Fact]
    public void DrainDomainEvents_StillYieldsTheEventsOfARemovedAggregate()
    {
        var tracker = ATracker();
        var aggregate = ANewList("Doomed");
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
        var aggregate = ANewList("Reconstructed elsewhere");

        tracker.MarkRemoved(aggregate, _mapper.ToNewRecord(aggregate));

        tracker.DrainDomainEvents().ShouldHaveSingleItem();
    }

    [Fact]
    public void MarkRemoved_KeepsTheTrackedRow_WhenTheAggregateIsAlreadyKnown()
    {
        var tracker = ATracker();
        var aggregate = ANewList("Doomed");
        var tracked = Track(tracker, aggregate);

        tracker.MarkRemoved(aggregate, new TodoListRecord { Id = aggregate.Id });

        tracker.FindRecord(aggregate.Id).ShouldBeSameAs(tracked);
    }

    [Fact]
    public void Restore_HandsTheEventsBackOnTheNextDrain()
    {
        var tracker = ATracker();
        var aggregate = ANewList("Groceries");
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
        var aggregate = ANewList("Groceries");
        var itemId = aggregate.AddItem("Buy milk", description: null);
        Track(tracker, aggregate);

        var created = tracker.DrainDomainEvents();
        created.ShouldHaveSingleItem();

        aggregate.CompleteItem(itemId, _now);
        tracker.Restore(created);

        tracker.DrainDomainEvents()
            .Select(raised => raised.GetType())
            .ShouldBe([typeof(TodoListCreatedDomainEvent), typeof(TodoItemCompletedDomainEvent)]);
    }

    [Fact]
    public void Restore_RejectsNull() => Should.Throw<ArgumentNullException>(() => ATracker().Restore(null!));

    private TodoListTracker ATracker() => new(_mapper);

    private static TodoList ANewList(string name) =>
        TodoList.Create(ATodoListAggregate.OwnerId, name, _now);

    private TodoListRecord Track(TodoListTracker tracker, TodoList aggregate)
    {
        var record = _mapper.ToNewRecord(aggregate);
        tracker.Track(aggregate, record);

        return record;
    }
}
