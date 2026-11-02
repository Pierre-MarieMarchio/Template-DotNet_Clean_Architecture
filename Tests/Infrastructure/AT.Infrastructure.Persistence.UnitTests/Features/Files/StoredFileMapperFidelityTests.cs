using System.Reflection;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

public sealed class StoredFileMapperFidelityTests
{
    private static readonly string[] _membersThatAreNotStoredState =
    [
        nameof(StoredFile.DomainEvents),
    ];

    [Fact]
    public void EveryPieceOfState_SurvivesTheRoundTrip()
    {
        var original = AStoredFileAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new StoredFileMapper(), original);

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

        properties.ShouldContain(nameof(StoredFile.Id));
        properties.ShouldContain(nameof(StoredFile.OwnerId));
        properties.ShouldContain(nameof(StoredFile.Category));
        properties.ShouldContain(nameof(StoredFile.ObjectKey));
        properties.ShouldContain(nameof(StoredFile.Name));
        properties.ShouldContain(nameof(StoredFile.DeclaredMediaType));
        properties.ShouldContain(nameof(StoredFile.Size));
        properties.ShouldContain(nameof(StoredFile.Checksum));
        properties.ShouldContain(nameof(StoredFile.State));
        properties.ShouldContain(nameof(StoredFile.RegisteredAt));
        properties.ShouldContain(nameof(StoredFile.AvailableAt));
        properties.ShouldContain(nameof(StoredFile.InspectionAttempts));
        properties.ShouldContain(nameof(StoredFile.NextInspectionAt));
        properties.ShouldContain(nameof(StoredFile.Version));
        properties.ShouldContain(nameof(StoredFile.CreatedAt));
        properties.ShouldContain(nameof(StoredFile.CreatedBy));
        properties.ShouldContain(nameof(StoredFile.LastModifiedAt));
        properties.ShouldContain(nameof(StoredFile.LastModifiedBy));
    }

    [Fact]
    public void TheHarness_DetectsAMapperThatForgetsAProperty()
    {
        var original = AStoredFileAggregate.FullyPopulated();

        var roundTripped = RoundTrip(new ForgetfulMapper(), original);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertStateSurvived(original, roundTripped));

        failure.Message.ShouldContain(nameof(StoredFile.ObjectKey));
    }

    private static StoredFile RoundTrip(IStoredFileMapper mapper, StoredFile original) =>
        mapper.ToAggregate(mapper.ToNewRecord(original));

    private static void AssertStateSurvived(StoredFile original, StoredFile roundTripped)
    {
        foreach (var property in StateProperties(_membersThatAreNotStoredState))
        {
            object? before = property.GetValue(original);
            object? after = property.GetValue(roundTripped);

            before.ShouldNotBe(
                DefaultOf(property.PropertyType),
                $"StoredFile.{property.Name} is at its type's default in the sample, so comparing it "
                + "before and after the round trip would pass even if the mapper never copied it. Give "
                + "it a distinctive value in AStoredFileAggregate.");

            after.ShouldBe(
                before,
                $"StoredFile.{property.Name} did not survive aggregate -> record -> aggregate. The mapper "
                + "is losing it silently: nothing throws, and the value simply comes back as its "
                + "default. Add it to StoredFileMapper in both directions.");
        }
    }

    private static IEnumerable<PropertyInfo> StateProperties(string[] excluded) =>
        typeof(StoredFile).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !excluded.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private static object? DefaultOf(Type type) =>
        type.IsValueType && Nullable.GetUnderlyingType(type) is null ? Activator.CreateInstance(type) : null;

    private sealed class ForgetfulMapper : IStoredFileMapper
    {
        private readonly StoredFileMapper _real = new();

        public StoredFile ToAggregate(StoredFileRecord record) => _real.ToAggregate(record);

        public StoredFileRecord ToNewRecord(StoredFile aggregate)
        {
            var record = _real.ToNewRecord(aggregate);

            record.ObjectKey = AStoredFileAggregate.OtherObjectKeyValue;

            return record;
        }

        public void WriteTo(StoredFile aggregate, StoredFileRecord record) => _real.WriteTo(aggregate, record);
    }
}
