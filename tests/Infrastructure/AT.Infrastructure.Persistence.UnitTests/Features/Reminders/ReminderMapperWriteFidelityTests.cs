using System.Reflection;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

public sealed class ReminderMapperWriteFidelityTests
{
    private static readonly string[] _membersWriteToDoesNotOwn =
    [
        nameof(Reminder.DomainEvents),

        nameof(Reminder.Version),

        nameof(Reminder.CreatedAt),
        nameof(Reminder.CreatedBy),
        nameof(Reminder.LastModifiedAt),
        nameof(Reminder.LastModifiedBy),
    ];

    private readonly ReminderMapper _mapper = new();

    [Fact]
    public void EveryDomainOwnedValue_ReachesTheTrackedRow()
    {
        var mutated = AReminderAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(_mapper, mutated);

        AssertStateWasWritten(mutated, written);
    }

    [Fact]
    public void TheExclusionList_NamesRealMembers()
    {
        foreach (string member in _membersWriteToDoesNotOwn)
        {
            StateProperties(excluded: [])
                .Select(property => property.Name)
                .ShouldContain(member, $"'{member}' is excluded from the write check but no longer exists.");
        }
    }

    [Fact]
    public void TheEnumeration_FindsTheStateItIsMeantToCheck()
    {
        var properties = StateProperties(_membersWriteToDoesNotOwn).Select(property => property.Name).ToList();

        properties.ShouldContain(nameof(Reminder.Id));
        properties.ShouldContain(nameof(Reminder.OwnerId));
        properties.ShouldContain(nameof(Reminder.TodoListId));
        properties.ShouldContain(nameof(Reminder.TodoItemId));
        properties.ShouldContain(nameof(Reminder.DueAt));
        properties.ShouldContain(nameof(Reminder.State));
        properties.ShouldContain(nameof(Reminder.NotifiedAt));
        properties.ShouldContain(nameof(Reminder.FailedAttempts));
        properties.ShouldContain(nameof(Reminder.NextAttemptAt));
    }

    [Fact]
    public void TheSample_DiffersFromTheStoredRowInEveryValueUnderTest()
    {
        var stored = AReminderAggregate.FullyPopulated();
        var mutated = AReminderAggregate.DifferentInEveryDomainOwnedValue();

        mutated.Id.ShouldBe(stored.Id, "the row is the same row; only its values move.");

        foreach (var property in StateProperties(_membersWriteToDoesNotOwn))
        {
            if (property.Name == nameof(Reminder.Id))
            {
                continue;
            }

            property.GetValue(mutated).ShouldNotBe(
                property.GetValue(stored),
                $"Reminder.{property.Name} is identical in both samples, so comparing it after WriteTo "
                + "would pass even if WriteTo never wrote it.");
        }
    }

    [Fact]
    public void TheHarness_DetectsAWriteThatForgetsAProperty()
    {
        var mutated = AReminderAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(new ForgetfulWriter(), mutated);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertStateWasWritten(mutated, written));

        failure.Message.ShouldContain(nameof(Reminder.DueAt));
    }

    private static Reminder WriteAndReadBack(IReminderMapper mapper, Reminder mutated)
    {
        var tracked = mapper.ToNewRecord(AReminderAggregate.FullyPopulated());

        mapper.WriteTo(mutated, tracked);

        return mapper.ToAggregate(tracked);
    }

    private static void AssertStateWasWritten(Reminder mutated, Reminder written)
    {
        foreach (var property in StateProperties(_membersWriteToDoesNotOwn))
        {
            property.GetValue(written).ShouldBe(
                property.GetValue(mutated),
                $"Reminder.{property.Name} did not survive aggregate -> tracked row -> aggregate. "
                + "WriteTo is losing it silently: the insert path carries it, so every round-trip test "
                + "still passes, and every update drops it. Assign it in WriteTo.");
        }
    }

    private static IEnumerable<PropertyInfo> StateProperties(string[] excluded) =>
        typeof(Reminder).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !excluded.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private sealed class ForgetfulWriter : IReminderMapper
    {
        private readonly ReminderMapper _real = new();

        public Reminder ToAggregate(ReminderRecord record) => _real.ToAggregate(record);

        public ReminderRecord ToNewRecord(Reminder aggregate) => _real.ToNewRecord(aggregate);

        public void WriteTo(Reminder aggregate, ReminderRecord record)
        {
            _real.WriteTo(aggregate, record);

            record.DueAt = AReminderAggregate.FullyPopulated().DueAt;
        }
    }
}
