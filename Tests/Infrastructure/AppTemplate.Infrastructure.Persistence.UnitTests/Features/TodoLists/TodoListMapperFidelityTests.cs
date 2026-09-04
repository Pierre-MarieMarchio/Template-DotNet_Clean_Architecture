using System.Reflection;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mappers;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;

/// <summary>
/// The guarantee that pays for the decision to keep persistence models separate from the domain model:
/// nothing is lost on the way through them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why reflection and not a list of assertions.</b> A hand-written round-trip test covers the
/// properties somebody remembered. The failure mode here is forgetting — a property added to the
/// aggregate and not to the mapper — so a test that has to be extended by hand fails in exactly the
/// situation it exists for. These tests enumerate the aggregate's state from the type itself, so a new
/// property is covered the moment it is declared, and the person who added it finds out from a red test
/// rather than from a support ticket.
/// </para>
/// <para>
/// <b>What makes them fail.</b> Three things, deliberately.
/// </para>
/// <list type="number">
/// <item><description>A property whose value differs after aggregate → record → aggregate. This is the
/// obvious one, and it catches an omission in either direction.</description></item>
/// <item><description>A property whose value in the sample is its type's <em>default</em>. Comparing
/// <c>null</c> to <c>null</c> proves nothing, so a sample that failed to exercise a property is itself a
/// failure — otherwise the nullable and optional properties, which are the likeliest to be forgotten,
/// would be the ones this test quietly exempted.</description></item>
/// <item><description>An entry in the exclusion list that no longer names a real property, which is how
/// a rename would otherwise turn "deliberately not compared" into "silently not compared".</description></item>
/// </list>
/// <para>
/// <see cref="TheHarness_DetectsAMapperThatForgetsAProperty"/> proves the machinery can actually fail, by
/// running the same comparison against a mapper with one line missing.
/// </para>
/// </remarks>
public sealed class TodoListMapperFidelityTests
{
    /// <summary>
    /// Aggregate-root members that are not stored state, each for a stated reason. Every name here is
    /// checked against the type, so this list cannot rot into a silent exemption.
    /// </summary>
    private static readonly string[] _rootMembersThatAreNotStoredState =
    [
        // Compared element by element instead, further down: a collection's reference equality says
        // nothing about whether its contents survived.
        nameof(TodoList.Items),

        // Deliberately not persisted. Events are raised, published after the commit and dropped; an
        // event that outlived the request would be delivered twice. Their survival across the mapping is
        // a different guarantee, covered by the integration tests that assert a consumer ran exactly
        // once.
        nameof(TodoList.DomainEvents),
    ];

    /// <summary>Item members compared separately, for the same reason as <c>Items</c> above.</summary>
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

    /// <summary>
    /// The exclusion lists above are only safe while every name in them still exists. A renamed property
    /// would otherwise stay excluded under its old name and simply stop being compared under its new one.
    /// </summary>
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

    /// <summary>
    /// Non-vacuity: the enumeration has to be finding the properties it claims to check. Without this, a
    /// reflection filter that matched nothing would make every assertion above pass over an empty loop.
    /// </summary>
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

    /// <summary>
    /// Proof that the harness can fail, which is the only thing that makes the tests above worth having.
    /// <see cref="ForgetfulMapper"/> is the real mapper with exactly one line removed — it does not carry
    /// the concurrency token — and the same reflection walk must reject it.
    /// <para>
    /// If this test ever passes, the comparison is comparing nothing and a mapper could quietly lose any
    /// property it liked.
    /// </para>
    /// </summary>
    [Fact]
    public void TheHarness_DetectsAMapperThatForgetsAProperty()
    {
        var original = ATodoListAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new ForgetfulMapper(), original);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertRootStateSurvived(original, roundTripped));

        failure.Message.ShouldContain(nameof(TodoList.Version));
    }

    // ---- The comparison ------------------------------------------------------------------------

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

            // The non-default requirement is enforced on the item that has every property populated. The
            // other item exists to prove the mapper handles absent values, and demanding a non-default
            // Description from it would be demanding that the sample stop covering that case.
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

    private static object? DefaultOf(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    /// <summary>
    /// The mapper as it would be if somebody forgot one line. Used only by
    /// <see cref="TheHarness_DetectsAMapperThatForgetsAProperty"/>, and it deliberately delegates
    /// everything else to the real implementation so that the one difference is the one thing under test.
    /// </summary>
    private sealed class ForgetfulMapper : ITodoListMapper
    {
        private readonly TodoListMapper _real = new();

        public TodoList ToAggregate(TodoListRecord record) => _real.ToAggregate(record);

        public TodoListRecord ToNewRecord(TodoList aggregate)
        {
            var record = _real.ToNewRecord(aggregate);

            // The forgotten line: the concurrency token never reaches the row.
            record.Version = 0u;

            return record;
        }

        public bool WriteTo(TodoList aggregate, TodoListRecord record) => _real.WriteTo(aggregate, record);
    }
}
