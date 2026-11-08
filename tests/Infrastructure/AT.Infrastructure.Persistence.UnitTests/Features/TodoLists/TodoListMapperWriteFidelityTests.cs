using System.Reflection;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

public sealed class TodoListMapperWriteFidelityTests
{
    private static readonly string[] _rootMembersWriteToDoesNotOwn =
    [
        nameof(TodoList.Items),

        nameof(TodoList.DomainEvents),

        nameof(TodoList.Version),

        nameof(TodoList.CreatedAt),
        nameof(TodoList.CreatedBy),
        nameof(TodoList.LastModifiedAt),
        nameof(TodoList.LastModifiedBy),
    ];

    private static readonly string[] _itemMembersWriteToDoesNotOwn =
    [
        nameof(TodoItem.Tags),
    ];

    private readonly TodoListMapper _mapper = new();

    [Fact]
    public void EveryDomainOwnedRootValue_ReachesTheTrackedRow()
    {
        var mutated = ATodoListAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(_mapper, mutated);

        AssertRootStateWasWritten(mutated, written);
    }

    [Fact]
    public void EveryDomainOwnedItemValue_ReachesTheTrackedRow()
    {
        var mutated = ATodoListAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(_mapper, mutated);

        AssertItemStateWasWritten(mutated, written);
    }

    [Fact]
    public void TheTagsOfEveryItem_ReachTheTrackedRow()
    {
        var mutated = ATodoListAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(_mapper, mutated);

        foreach (var item in mutated.Items)
        {
            written.Items.Single(candidate => candidate.Id == item.Id).Tags
                .Select(tag => tag.Value)
                .ShouldBe(
                    item.Tags.Select(tag => tag.Value),
                    ignoreOrder: true,
                    $"The tags of item '{item.Title.Value}' did not reach its row.");
        }
    }

    [Fact]
    public void TheExclusionLists_NameRealMembers()
    {
        foreach (string member in _rootMembersWriteToDoesNotOwn)
        {
            StateProperties(typeof(TodoList), excluded: [])
                .Select(property => property.Name)
                .ShouldContain(member, $"'{member}' is excluded from the write check but no longer exists.");
        }

        foreach (string member in _itemMembersWriteToDoesNotOwn)
        {
            StateProperties(typeof(TodoItem), excluded: [])
                .Select(property => property.Name)
                .ShouldContain(member, $"'{member}' is excluded from the write check but no longer exists.");
        }
    }

    [Fact]
    public void TheEnumeration_FindsTheStateItIsMeantToCheck()
    {
        var rootProperties = StateProperties(typeof(TodoList), _rootMembersWriteToDoesNotOwn)
            .Select(property => property.Name)
            .ToList();

        rootProperties.ShouldContain(nameof(TodoList.Id));
        rootProperties.ShouldContain(nameof(TodoList.OwnerId));
        rootProperties.ShouldContain(nameof(TodoList.Name));

        var itemProperties = StateProperties(typeof(TodoItem), _itemMembersWriteToDoesNotOwn)
            .Select(property => property.Name)
            .ToList();

        itemProperties.ShouldContain(nameof(TodoItem.Id));
        itemProperties.ShouldContain(nameof(TodoItem.TodoListId));
        itemProperties.ShouldContain(nameof(TodoItem.Title));
        itemProperties.ShouldContain(nameof(TodoItem.Description));
        itemProperties.ShouldContain(nameof(TodoItem.CompletedAt));
    }

    [Fact]
    public void TheSample_DiffersFromTheStoredRowInEveryValueUnderTest()
    {
        var stored = ATodoListAggregate.FullyPopulated();
        var mutated = ATodoListAggregate.DifferentInEveryDomainOwnedValue();

        mutated.Id.ShouldBe(stored.Id, "the row is the same row; only its values move.");

        foreach (var property in StateProperties(typeof(TodoList), _rootMembersWriteToDoesNotOwn))
        {
            if (property.Name == nameof(TodoList.Id))
            {
                continue;
            }

            property.GetValue(mutated).ShouldNotBe(
                property.GetValue(stored),
                $"TodoList.{property.Name} is identical in both samples, so comparing it after WriteTo "
                + "would pass even if WriteTo never wrote it.");
        }

        foreach (var item in mutated.Items)
        {
            var before = stored.Items.Single(candidate => candidate.Id == item.Id);

            foreach (var property in StateProperties(typeof(TodoItem), _itemMembersWriteToDoesNotOwn))
            {
                if (property.Name is nameof(TodoItem.Id) or nameof(TodoItem.TodoListId))
                {
                    continue;
                }

                property.GetValue(item).ShouldNotBe(
                    property.GetValue(before),
                    $"TodoItem.{property.Name} is identical in both samples, so comparing it after "
                    + "WriteTo would pass even if WriteTo never wrote it.");
            }
        }
    }

    [Fact]
    public void TheHarness_DetectsAWriteThatForgetsAProperty()
    {
        var mutated = ATodoListAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(new ForgetfulWriter(), mutated);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertItemStateWasWritten(mutated, written));

        failure.Message.ShouldContain(nameof(TodoItem.Description));
    }

    private static TodoList WriteAndReadBack(ITodoListMapper mapper, TodoList mutated)
    {
        var tracked = mapper.ToNewRecord(ATodoListAggregate.FullyPopulated());

        mapper.WriteTo(mutated, tracked);

        return mapper.ToAggregate(tracked);
    }

    private static void AssertRootStateWasWritten(TodoList mutated, TodoList written)
    {
        foreach (var property in StateProperties(typeof(TodoList), _rootMembersWriteToDoesNotOwn))
        {
            AssertPropertyWasWritten(property, mutated, written);
        }
    }

    private static void AssertItemStateWasWritten(TodoList mutated, TodoList written)
    {
        var properties = StateProperties(typeof(TodoItem), _itemMembersWriteToDoesNotOwn).ToList();

        foreach (var item in mutated.Items)
        {
            var stored = written.Items.Single(candidate => candidate.Id == item.Id);

            foreach (var property in properties)
            {
                AssertPropertyWasWritten(property, item, stored);
            }
        }
    }

    private static void AssertPropertyWasWritten(PropertyInfo property, object mutated, object written) =>
        property.GetValue(written).ShouldBe(
            property.GetValue(mutated),
            $"{property.DeclaringType?.Name}.{property.Name} did not survive aggregate -> tracked row -> "
            + "aggregate. WriteTo is losing it silently: the insert path carries it, so every "
            + "round-trip test still passes, and every update drops it. Assign it in WriteTo.");

    private static IEnumerable<PropertyInfo> StateProperties(Type type, string[] excluded) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !excluded.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private sealed class ForgetfulWriter : ITodoListMapper
    {
        private readonly TodoListMapper _real = new();

        public TodoList ToAggregate(TodoListRecord record) => _real.ToAggregate(record);

        public TodoListRecord ToNewRecord(TodoList aggregate) => _real.ToNewRecord(aggregate);

        public bool WriteTo(TodoList aggregate, TodoListRecord record)
        {
            bool structureChanged = _real.WriteTo(aggregate, record);

            foreach (var item in record.Items)
            {
                item.Description = null;
            }

            return structureChanged;
        }
    }
}
