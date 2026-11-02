using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Model.Events;

public sealed class TodoItemCompletedDomainEventTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static TodoItemCompletedDomainEvent CompleteAnItem(
        out TodoList list,
        out Guid itemId,
        string title = "Buy milk",
        DateTimeOffset? completedAt = null)
    {
        list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", _now);
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
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", _now);
        var itemId = list.AddItem("Buy milk", null);
        list.ClearDomainEvents();

        list.CompleteItem(itemId, _now);
        list.ReopenItem(itemId, _now);
        list.CompleteItem(itemId, _now.AddDays(1));

        list.DomainEvents.OfType<TodoItemCompletedDomainEvent>().Count().ShouldBe(2);
    }
}
