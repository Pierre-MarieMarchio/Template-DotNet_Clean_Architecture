using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Events;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Events;

/// <summary>
/// What the creation event carries, asserted through the aggregate that raises it —
/// a handler runs after the transaction has committed and can only see these values, so
/// a payload wired up to the wrong field is a defect no other test would catch.
/// </summary>
public sealed class TodoListCreatedDomainEventTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static TodoListCreatedDomainEvent EventRaisedBy(TodoList list) =>
        list.DomainEvents.OfType<TodoListCreatedDomainEvent>().Single();

    [Fact]
    public void TheEvent_CarriesTheIdOfTheListThatWasCreated()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);

        EventRaisedBy(list).TodoListId.ShouldBe(list.Id);
    }

    [Fact]
    public void TheEvent_CarriesTheOwner()
    {
        var ownerId = Guid.CreateVersion7();

        var list = TodoList.Create(ownerId, "Groceries", _now);

        EventRaisedBy(list).OwnerId.ShouldBe(ownerId);
    }

    [Fact]
    public void TheEvent_CarriesTheNormalisedName()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "  Groceries  ", _now);

        EventRaisedBy(list).Name.ShouldBe("Groceries");
    }

    /// <summary>
    /// The timestamp is the one handed to the aggregate, never one read from the ambient
    /// clock — that is what makes the behaviour reproducible.
    /// </summary>
    [Fact]
    public void TheEvent_CarriesTheInjectedInstant()
    {
        var instant = new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero);

        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", instant);

        EventRaisedBy(list).OccurredOn.ShouldBe(instant);
    }

    [Fact]
    public void TheEvent_IsADomainEvent()
    {
        var list = TodoList.Create(Guid.CreateVersion7(), "Groceries", _now);

        EventRaisedBy(list).ShouldBeAssignableTo<IDomainEvent>();
    }

    /// <summary>
    /// The event carries values rather than the aggregate, so a handler cannot reach back
    /// into the model after the transaction has committed.
    /// </summary>
    [Fact]
    public void TheEvent_ExposesNoReferenceToTheAggregate() =>
        typeof(TodoListCreatedDomainEvent).GetProperties()
            .ShouldNotContain(property => property.PropertyType == typeof(TodoList));
}
