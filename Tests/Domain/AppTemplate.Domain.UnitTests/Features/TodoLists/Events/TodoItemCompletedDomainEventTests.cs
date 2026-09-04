using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Events;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Events;

/// <summary>
/// A handler runs after the transaction has committed and can only see the values on the event,
/// so a payload wired up to the wrong field is a defect no other test would catch.
/// </summary>
public sealed class TodoItemCompletedDomainEventTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static TodoItemCompletedDomainEvent CompleteAnItem(
        out TodoList list,
        out Guid itemId,
        string title = "Buy milk",
        DateTimeOffset? completedAt = null)
    {
        list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);
        itemId = list.AddItem(title, null);
        list.ClearDomainEvents();
        list.CompleteItem(itemId, completedAt ?? _now);

        return list.DomainEvents.OfType<TodoItemCompletedDomainEvent>().Single();
    }

    [Fact]
    public void TheEvent_IdentifiesBothTheListAndTheItem()
    {
        var domainEvent = CompleteAnItem(out var list, out var itemId);

        domainEvent.TodoListId.ShouldBe(list.Id);
        domainEvent.TodoItemId.ShouldBe(itemId);
    }

    /// <summary>
    /// The title lets a handler describe what happened without loading the aggregate
    /// again, and it is the stored (normalised) title, not the caller's raw input.
    /// </summary>
    [Fact]
    public void TheEvent_CarriesTheNormalisedItemTitle()
    {
        var domainEvent = CompleteAnItem(out _, out _, title: "  Buy milk  ");

        domainEvent.Title.ShouldBe("Buy milk");
    }

    [Fact]
    public void TheEvent_CarriesTheCompletionInstantItWasGiven()
    {
        var completedAt = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var domainEvent = CompleteAnItem(out _, out _, completedAt: completedAt);

        domainEvent.OccurredOn.ShouldBe(completedAt);
    }

    /// <summary>
    /// The event's instant and the item's stored completion time are the same value, so a
    /// consumer of the event and a reader of the aggregate never disagree.
    /// </summary>
    [Fact]
    public void TheEventInstant_MatchesTheItemsStoredCompletionTime()
    {
        var completedAt = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var domainEvent = CompleteAnItem(out var list, out var itemId, completedAt: completedAt);

        list.Items.Single(item => item.Id == itemId).CompletedAt.ShouldBe(domainEvent.OccurredOn);
    }

    [Fact]
    public void TheEvent_IsADomainEvent() =>
        CompleteAnItem(out _, out _).ShouldBeAssignableTo<IDomainEvent>();

    [Fact]
    public void TheEvent_ExposesNoReferenceToTheAggregateOrItsItems()
    {
        var properties = typeof(TodoItemCompletedDomainEvent).GetProperties();

        properties.ShouldNotContain(property => property.PropertyType == typeof(TodoList));
        properties.ShouldNotContain(property => property.PropertyType == typeof(TodoItem));
    }

    [Fact]
    public void OneEvent_IsRaisedPerCompletion()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);
        var itemId = list.AddItem("Buy milk", null);
        list.ClearDomainEvents();

        list.CompleteItem(itemId, _now);
        list.ReopenItem(itemId);
        list.CompleteItem(itemId, _now.AddDays(1));

        list.DomainEvents.OfType<TodoItemCompletedDomainEvent>().Count().ShouldBe(2);
    }
}
