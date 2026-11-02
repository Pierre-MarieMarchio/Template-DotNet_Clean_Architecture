using System.Reflection;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

public sealed class ReminderMapperFidelityTests
{
    private static readonly string[] _membersThatAreNotStoredState =
    [
        nameof(Reminder.DomainEvents),
    ];

    [Fact]
    public void EveryPieceOfState_SurvivesTheRoundTrip()
    {
        var original = AReminderAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new ReminderMapper(), original);

        AssertStateSurvived(original, roundTripped);
    }

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
        properties.ShouldContain(nameof(Reminder.NotifiedAt));
        properties.ShouldContain(nameof(Reminder.FailedAttempts));
        properties.ShouldContain(nameof(Reminder.NextAttemptAt));
        properties.ShouldContain(nameof(Reminder.Version));
        properties.ShouldContain(nameof(Reminder.CreatedAt));
        properties.ShouldContain(nameof(Reminder.CreatedBy));
        properties.ShouldContain(nameof(Reminder.LastModifiedAt));
        properties.ShouldContain(nameof(Reminder.LastModifiedBy));
    }

    [Fact]
    public void TheHarness_DetectsAMapperThatForgetsAProperty()
    {
        var original = AReminderAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new ForgetfulMapper(), original);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertStateSurvived(original, roundTripped));

        failure.Message.ShouldContain(nameof(Reminder.Version));
    }

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

    private static IEnumerable<PropertyInfo> StateProperties(string[] excluded) =>
        typeof(Reminder).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !excluded.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private static object? DefaultOf(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    private sealed class ForgetfulMapper : IReminderMapper
    {
        private readonly ReminderMapper _real = new();

        public Reminder ToAggregate(ReminderRecord record) => _real.ToAggregate(record);

        public ReminderRecord ToNewRecord(Reminder aggregate)
        {
            var record = _real.ToNewRecord(aggregate);

            record.Version = 0u;

            return record;
        }

        public void WriteTo(Reminder aggregate, ReminderRecord record) => _real.WriteTo(aggregate, record);
    }
}
