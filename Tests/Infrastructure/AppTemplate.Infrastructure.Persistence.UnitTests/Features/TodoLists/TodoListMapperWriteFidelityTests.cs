using System.Reflection;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

/// <summary>
/// The same fidelity guarantee as <see cref="TodoListMapperFidelityTests"/>, for the path every command
/// actually takes: writing an aggregate onto rows that already exist.
/// </summary>
/// <remarks>
/// <para>
/// A round trip through <c>ToNewRecord</c> only proves the <em>insert</em> path is total. A property
/// added to the aggregate and to <c>ToNewRecord</c> but forgotten in <c>WriteTo</c> passes that check
/// completely: the insert carries it, the read brings it back, and every update silently drops it. This
/// is the mirror, and it is the one that matters — an aggregate is inserted once and updated forever.
/// </para>
/// <para>
/// The store-owned columns are excluded by name, because <c>WriteTo</c> is deliberately partial: the
/// concurrency token belongs to PostgreSQL and the audit stamps to the interceptor. Every excluded name
/// is checked against the type, so a rename cannot turn "deliberately not written" into "silently not
/// written".
/// </para>
/// </remarks>
public sealed class TodoListMapperWriteFidelityTests
{
    /// <summary>
    /// Root members <c>WriteTo</c> does not write, each for a stated reason.
    /// </summary>
    private static readonly string[] _rootMembersWriteToDoesNotOwn =
    [
        // Compared element by element further down.
        nameof(TodoList.Items),

        // Not persisted at all: events are published after the commit and dropped.
        nameof(TodoList.DomainEvents),

        // PostgreSQL's xmin. A second writer would be a second opinion.
        nameof(TodoList.Version),

        // The audit interceptor's four values, flowing row -> aggregate and never the other way.
        nameof(TodoList.CreatedAt),
        nameof(TodoList.CreatedBy),
        nameof(TodoList.LastModifiedAt),
        nameof(TodoList.LastModifiedBy),
    ];

    /// <summary>Item members compared separately, for the same reason as <c>Items</c> above.</summary>
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

    /// <summary>
    /// Non-vacuity: the enumeration has to be finding the properties it claims to check, or every
    /// assertion above would pass over an empty loop.
    /// </summary>
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

    /// <summary>
    /// The sample has to differ from the stored row in every value being compared. Comparing a value
    /// against itself passes for a property <c>WriteTo</c> never touched, which is the whole class of
    /// defect these tests exist for.
    /// </summary>
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

    /// <summary>
    /// Proof that the harness can fail. <see cref="ForgetfulWriter"/> is the real mapper with one
    /// assignment undone — an item's description never reaches its row — and the same walk must reject
    /// it. A green test nobody has seen go red is not evidence of anything.
    /// </summary>
    [Fact]
    public void TheHarness_DetectsAWriteThatForgetsAProperty()
    {
        var mutated = ATodoListAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(new ForgetfulWriter(), mutated);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertItemStateWasWritten(mutated, written));

        failure.Message.ShouldContain(nameof(TodoItem.Description));
    }

    // ---- The comparison ------------------------------------------------------------------------

    /// <summary>
    /// Stages <paramref name="mutated"/> onto the row a query would have produced for
    /// <see cref="ATodoListAggregate.FullyPopulated"/>, then reads that row back as an aggregate. The
    /// row is built by the real mapper, so the fixture cannot disagree with the schema.
    /// </summary>
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

    /// <summary>
    /// The readable state of a domain type: public instance properties with a getter, minus the ones
    /// excluded by name. Indexers are skipped because they are not state.
    /// </summary>
    private static IEnumerable<PropertyInfo> StateProperties(Type type, string[] excluded) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !excluded.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    /// <summary>
    /// The mapper as it would be if somebody forgot one assignment in the update path. Everything else
    /// delegates to the real implementation, so the one difference is the one thing under test.
    /// </summary>
    private sealed class ForgetfulWriter : ITodoListMapper
    {
        private readonly TodoListMapper _real = new();

        public TodoList ToAggregate(TodoListRecord record) => _real.ToAggregate(record);

        public TodoListRecord ToNewRecord(TodoList aggregate) => _real.ToNewRecord(aggregate);

        public bool WriteTo(TodoList aggregate, TodoListRecord record)
        {
            bool structureChanged = _real.WriteTo(aggregate, record);

            // The forgotten line: an item's description never reaches its row.
            foreach (var item in record.Items)
            {
                item.Description = null;
            }

            return structureChanged;
        }
    }
}
