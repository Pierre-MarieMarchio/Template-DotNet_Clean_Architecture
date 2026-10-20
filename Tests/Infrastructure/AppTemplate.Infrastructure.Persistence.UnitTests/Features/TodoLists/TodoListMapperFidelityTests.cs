using System.Reflection;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

public sealed class TodoListMapperFidelityTests
{
    private static readonly string[] _rootMembersThatAreNotStoredState =
    [
        nameof(TodoList.Items),

        nameof(TodoList.DomainEvents),
    ];

    private static readonly string[] _itemMembersThatAreNotStoredState =
    [
        nameof(TodoItem.Tags),
    ];

    [Fact]
    public void EveryPieceOfRootState_SurvivesTheRoundTrip()
    {
        var original = ATodoListAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new TodoListMapper(), original);

        AssertRootStateSurvived(original, roundTripped);
    }

    [Fact]
    public void EveryPieceOfItemState_SurvivesTheRoundTrip()
    {
        var original = ATodoListAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new TodoListMapper(), original);

        AssertItemStateSurvived(original, roundTripped);
    }

    [Fact]
    public void TheItemsAndTheirTags_SurviveAsSets()
    {
        var original = ATodoListAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new TodoListMapper(), original);

        roundTripped.Items.Select(item => item.Id)
            .ShouldBe(original.Items.Select(item => item.Id), ignoreOrder: true);

        foreach (var item in original.Items)
        {
            var mapped = roundTripped.Items.Single(candidate => candidate.Id == item.Id);

            mapped.Tags.Select(tag => tag.Value)
                .ShouldBe(
                    item.Tags.Select(tag => tag.Value),
                    ignoreOrder: true,
                    $"The tags of item '{item.Title}' did not survive the round trip.");
        }
    }

    [Fact]
    public void TheExclusionLists_NameRealMembers()
    {
        foreach (string member in _rootMembersThatAreNotStoredState)
        {
            StateProperties(typeof(TodoList), excluded: [])
                .Select(property => property.Name)
                .ShouldContain(member, $"'{member}' is excluded from the fidelity check but no longer exists.");
        }

        foreach (string member in _itemMembersThatAreNotStoredState)
        {
            StateProperties(typeof(TodoItem), excluded: [])
                .Select(property => property.Name)
                .ShouldContain(member, $"'{member}' is excluded from the fidelity check but no longer exists.");
        }
    }

    [Fact]
    public void TheEnumeration_FindsTheStateItIsMeantToCheck()
    {
        var rootProperties = StateProperties(typeof(TodoList), _rootMembersThatAreNotStoredState)
            .Select(property => property.Name)
            .ToList();

        rootProperties.ShouldContain(nameof(TodoList.Id));
        rootProperties.ShouldContain(nameof(TodoList.OwnerId));
        rootProperties.ShouldContain(nameof(TodoList.Name));
        rootProperties.ShouldContain(nameof(TodoList.Version));
        rootProperties.ShouldContain(nameof(TodoList.CreatedAt));
        rootProperties.ShouldContain(nameof(TodoList.CreatedBy));
        rootProperties.ShouldContain(nameof(TodoList.LastModifiedAt));
        rootProperties.ShouldContain(nameof(TodoList.LastModifiedBy));

        var itemProperties = StateProperties(typeof(TodoItem), _itemMembersThatAreNotStoredState)
            .Select(property => property.Name)
            .ToList();

        itemProperties.ShouldContain(nameof(TodoItem.Id));
        itemProperties.ShouldContain(nameof(TodoItem.TodoListId));
        itemProperties.ShouldContain(nameof(TodoItem.Title));
        itemProperties.ShouldContain(nameof(TodoItem.Description));
        itemProperties.ShouldContain(nameof(TodoItem.CompletedAt));
    }

    [Fact]
    public void TheHarness_DetectsAMapperThatForgetsAProperty()
    {
        var original = ATodoListAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new ForgetfulMapper(), original);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertRootStateSurvived(original, roundTripped));

        failure.Message.ShouldContain(nameof(TodoList.Version));
    }

    private static TodoList RoundTrip(ITodoListMapper mapper, TodoList original) =>
        mapper.ToAggregate(mapper.ToNewRecord(original));

    private static void AssertRootStateSurvived(TodoList original, TodoList roundTripped)
    {
        foreach (var property in StateProperties(typeof(TodoList), _rootMembersThatAreNotStoredState))
        {
            AssertPropertySurvived(property, original, roundTripped, requireNonDefault: true);
        }
    }

    private static void AssertItemStateSurvived(TodoList original, TodoList roundTripped)
    {
        var properties = StateProperties(typeof(TodoItem), _itemMembersThatAreNotStoredState).ToList();

        foreach (var item in original.Items)
        {
            var mapped = roundTripped.Items.Single(candidate => candidate.Id == item.Id);

            bool isTheFullyPopulatedItem = string.Equals(
                item.Title.Value,
                ATodoListAggregate.CompletedItemTitle,
                StringComparison.Ordinal);

            foreach (var property in properties)
            {
                AssertPropertySurvived(property, item, mapped, isTheFullyPopulatedItem);
            }
        }
    }

    private static void AssertPropertySurvived(
        PropertyInfo property,
        object original,
        object roundTripped,
        bool requireNonDefault)
    {
        object? before = property.GetValue(original);
        object? after = property.GetValue(roundTripped);

        if (requireNonDefault)
        {
            before.ShouldNotBe(
                DefaultOf(property.PropertyType),
                $"{property.DeclaringType?.Name}.{property.Name} is at its type's default in the sample, so "
                + "comparing it before and after the round trip would pass even if the mapper never "
                + "copied it. Give it a distinctive value in ATodoListAggregate.");
        }

        after.ShouldBe(
            before,
            $"{property.DeclaringType?.Name}.{property.Name} did not survive aggregate -> record -> "
            + "aggregate. The mapper is losing it silently: nothing throws, and the value simply comes "
            + "back as its default. Add it to TodoListMapper in both directions.");
    }

    private static IEnumerable<PropertyInfo> StateProperties(Type type, string[] excluded) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !excluded.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private static object? DefaultOf(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    private sealed class ForgetfulMapper : ITodoListMapper
    {
        private readonly TodoListMapper _real = new();

        public TodoList ToAggregate(TodoListRecord record) => _real.ToAggregate(record);

        public TodoListRecord ToNewRecord(TodoList aggregate)
        {
            var record = _real.ToNewRecord(aggregate);

            record.Version = 0u;

            return record;
        }

        public bool WriteTo(TodoList aggregate, TodoListRecord record) => _real.WriteTo(aggregate, record);
    }
}
