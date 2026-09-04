using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Events;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Events;

/// <summary>Mirrors <see cref="TodoItemCompletedDomainEventTests"/>: a consumer tracking
/// completions must be able to track reversals the same way.</summary>
public sealed class TodoItemReopenedDomainEventTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static TodoItemReopenedDomainEvent ReopenACompletedItem(
        out TodoList list,
        out Guid itemId,
        string title = "Buy milk",
        DateTimeOffset? reopenedAt = null)
    {
        list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);
        itemId = list.AddItem(title, null);
        list.CompleteItem(itemId, _now);
        list.ClearDomainEvents();
        list.ReopenItem(itemId, reopenedAt ?? _now);

        return list.DomainEvents.OfType<TodoItemReopenedDomainEvent>().Single();
    }

    [Fact]
    public void TheEvent_IdentifiesBothTheListAndTheItem()
    {
        var domainEvent = ReopenACompletedItem(out var list, out var itemId);

        domainEvent.TodoListId.ShouldBe(list.Id);
        domainEvent.TodoItemId.ShouldBe(itemId);
    }

    [Fact]
    public void TheEvent_CarriesTheNormalisedItemTitle()
    {
        var domainEvent = ReopenACompletedItem(out _, out _, title: "  Buy milk  ");

        domainEvent.Title.ShouldBe("Buy milk");
    }

    [Fact]
    public void TheEvent_CarriesTheReopeningInstantItWasGiven()
    {
        var reopenedAt = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var domainEvent = ReopenACompletedItem(out _, out _, reopenedAt: reopenedAt);

        domainEvent.OccurredOn.ShouldBe(reopenedAt);
    }

    [Fact]
    public void TheEvent_IsADomainEvent() =>
        ReopenACompletedItem(out _, out _).ShouldBeAssignableTo<IDomainEvent>();

    [Fact]
    public void TheEvent_ExposesNoReferenceToTheAggregateOrItsItems()
    {
        var properties = typeof(TodoItemReopenedDomainEvent).GetProperties();

        properties.ShouldNotContain(property => property.PropertyType == typeof(TodoList));
        properties.ShouldNotContain(property => property.PropertyType == typeof(TodoItem));
    }

    /// <summary>No event, hence nothing to find, when the reopen was a no-op.</summary>
    [Fact]
    public void NoEvent_IsRaisedForAnItemThatWasAlreadyOpen()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);
        var itemId = list.AddItem("Buy milk", null);
        list.ClearDomainEvents();

        list.ReopenItem(itemId, _now);

        list.DomainEvents.OfType<TodoItemReopenedDomainEvent>().ShouldBeEmpty();
    }
}
