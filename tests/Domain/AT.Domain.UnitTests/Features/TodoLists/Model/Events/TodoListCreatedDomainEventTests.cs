using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.Model.Events;

public sealed class TodoListCreatedDomainEventTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static TodoListCreatedDomainEvent EventRaisedBy(TodoList list) =>
        list.DomainEvents.OfType<TodoListCreatedDomainEvent>().Single();

    [Fact]
    public void TheEvent_CarriesTheIdOfTheListThatWasCreated()
    {
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", _now);

        EventRaisedBy(list).TodoListId.ShouldBe(list.Id);
    }

    [Fact]
    public void TheEvent_CarriesTheOwner()
    {
        var ownerId = UserId.Create(Guid.CreateVersion7());

        var list = TodoList.Create(ownerId, "Groceries", _now);

        EventRaisedBy(list).OwnerId.ShouldBe(ownerId);
    }

    [Fact]
    public void TheEvent_CarriesTheNormalisedName()
    {
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "  Groceries  ", _now);

        EventRaisedBy(list).Name.ShouldBe("Groceries");
    }

    [Fact]
    public void TheEvent_CarriesTheInjectedInstant()
    {
        var instant = new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero);

        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", instant);

        EventRaisedBy(list).OccurredOn.ShouldBe(instant);
    }

    [Fact]
    public void TheEvent_IsADomainEvent()
    {
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Groceries", _now);

        EventRaisedBy(list).ShouldBeAssignableTo<IDomainEvent>();
    }

    [Fact]
    public void TheEvent_ExposesNoReferenceToTheAggregate() =>
        typeof(TodoListCreatedDomainEvent).GetProperties()
            .ShouldNotContain(property => property.PropertyType == typeof(TodoList));
}
