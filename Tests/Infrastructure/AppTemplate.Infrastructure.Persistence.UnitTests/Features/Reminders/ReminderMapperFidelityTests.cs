using System.Reflection;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

/// <summary>
/// The guarantee that pays for the decision to keep persistence models separate from the domain model:
/// nothing is lost on the way through them.
/// </summary>
/// <remarks>
/// <para>
/// The same reflection harness as <see cref="TodoLists.TodoListMapperFidelityTests"/>, which states
/// why the aggregate's state is enumerated from the type rather than asserted by hand, and the three
/// things that make it fail.
/// </para>
/// <see cref="TheHarness_DetectsAMapperThatForgetsAProperty"/> proves the machinery can actually fail, by
/// running the same comparison against a mapper with one line missing.
/// </remarks>
public sealed class ReminderMapperFidelityTests
{
    /// <summary>
    /// Aggregate-root members that are not stored state. Checked against the type below, so this list
    /// cannot rot into a silent exemption.
    /// </summary>
    private static readonly string[] _membersThatAreNotStoredState =
    [
        // Deliberately not persisted. Events are raised, published after the commit and dropped; an
        // event that outlived the request would be delivered twice.
        nameof(Reminder.DomainEvents),
    ];

    [Fact]
    public void EveryPieceOfState_SurvivesTheRoundTrip()
    {
        var original = AReminderAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new ReminderMapper(), original);

        AssertStateSurvived(original, roundTripped);
    }

    /// <summary>
    /// The exclusion list is only safe while every name in it still exists. A renamed property would
    /// otherwise stay excluded under its old name and simply stop being compared under its new one.
    /// </summary>
    [Fact]
    public void TheExclusionList_NamesRealMembers()
    {
        foreach (string member in _membersThatAreNotStoredState)
        {
            StateProperties(excluded: [])
                .Select(property => property.Name)
                .ShouldContain(member, $"'{member}' is excluded from the fidelity check but no longer exists.");
        }
    }

    /// <summary>
    /// Non-vacuity: the enumeration has to be finding the properties it claims to check. Without this, a
    /// reflection filter that matched nothing would make the assertion above pass over an empty loop.
    /// </summary>
    [Fact]
    public void TheEnumeration_FindsTheStateItIsMeantToCheck()
    {
        var properties = StateProperties(_membersThatAreNotStoredState).Select(property => property.Name).ToList();

        properties.ShouldContain(nameof(Reminder.Id));
        properties.ShouldContain(nameof(Reminder.OwnerId));
        properties.ShouldContain(nameof(Reminder.TodoListId));
        properties.ShouldContain(nameof(Reminder.TodoItemId));
        properties.ShouldContain(nameof(Reminder.DueAt));
        properties.ShouldContain(nameof(Reminder.State));
        properties.ShouldContain(nameof(Reminder.ClaimedAt));
        properties.ShouldContain(nameof(Reminder.NotifiedAt));
        properties.ShouldContain(nameof(Reminder.Version));
        properties.ShouldContain(nameof(Reminder.CreatedAt));
        properties.ShouldContain(nameof(Reminder.CreatedBy));
        properties.ShouldContain(nameof(Reminder.LastModifiedAt));
        properties.ShouldContain(nameof(Reminder.LastModifiedBy));
    }

    /// <summary>
    /// Proof that the harness can fail. <see cref="ForgetfulMapper"/> is the real mapper with exactly one
    /// line removed — it does not carry the concurrency token — and the same reflection walk must reject
    /// it. If this test ever passes, the comparison is comparing nothing and a mapper could quietly lose
    /// any property it liked.
    /// </summary>
    [Fact]
    public void TheHarness_DetectsAMapperThatForgetsAProperty()
    {
        var original = AReminderAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new ForgetfulMapper(), original);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertStateSurvived(original, roundTripped));

        failure.Message.ShouldContain(nameof(Reminder.Version));
    }

    // ---- The comparison ------------------------------------------------------------------------

    private static Reminder RoundTrip(IReminderMapper mapper, Reminder original) =>
        mapper.ToAggregate(mapper.ToNewRecord(original));

    private static void AssertStateSurvived(Reminder original, Reminder roundTripped)
    {
        foreach (var property in StateProperties(_membersThatAreNotStoredState))
        {
            object? before = property.GetValue(original);
            object? after = property.GetValue(roundTripped);

            before.ShouldNotBe(
                DefaultOf(property.PropertyType),
                $"Reminder.{property.Name} is at its type's default in the sample, so comparing it "
                + "before and after the round trip would pass even if the mapper never copied it. Give "
                + "it a distinctive value in AReminderAggregate.");

            after.ShouldBe(
                before,
                $"Reminder.{property.Name} did not survive aggregate -> record -> aggregate. The mapper "
                + "is losing it silently: nothing throws, and the value simply comes back as its "
                + "default. Add it to ReminderMapper in both directions.");
        }
    }

    /// <summary>
    /// The readable state of the aggregate: public instance properties with a getter, minus the ones
    /// excluded by name. Indexers are skipped because they are not state.
    /// </summary>
    private static IEnumerable<PropertyInfo> StateProperties(string[] excluded) =>
        typeof(Reminder).GetProperties(BindingFlags.Public | BindingFlags.Instance)
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
    private sealed class ForgetfulMapper : IReminderMapper
    {
        private readonly ReminderMapper _real = new();

        public Reminder ToAggregate(ReminderRecord record) => _real.ToAggregate(record);

        public ReminderRecord ToNewRecord(Reminder aggregate)
        {
            var record = _real.ToNewRecord(aggregate);

            // The forgotten line: the concurrency token never reaches the row.
            record.Version = 0u;

            return record;
        }

        public void WriteTo(Reminder aggregate, ReminderRecord record) => _real.WriteTo(aggregate, record);
    }
}
