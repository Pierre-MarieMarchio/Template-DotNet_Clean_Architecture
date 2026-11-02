using System.Reflection;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

public sealed class StoredFileMapperWriteFidelityTests
{
    private static readonly string[] _membersWriteToDoesNotOwn =
    [
        nameof(StoredFile.DomainEvents),

        nameof(StoredFile.Version),

        nameof(StoredFile.CreatedAt),
        nameof(StoredFile.CreatedBy),
        nameof(StoredFile.LastModifiedAt),
        nameof(StoredFile.LastModifiedBy),
    ];

    private readonly StoredFileMapper _mapper = new();

    [Fact]
    public void EveryDomainOwnedValue_ReachesTheTrackedRow()
    {
        var mutated = AStoredFileAggregate.DifferentInEveryDomainOwnedValue();

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
    }

    [Fact]
    public void TheSample_DiffersFromTheStoredRowInEveryValueUnderTest()
    {
        var stored = AStoredFileAggregate.FullyPopulated();
        var mutated = AStoredFileAggregate.DifferentInEveryDomainOwnedValue();

        mutated.Id.ShouldBe(stored.Id, "the row is the same row; only its values move.");

        foreach (var property in StateProperties(_membersWriteToDoesNotOwn))
        {
            if (property.Name == nameof(StoredFile.Id))
            {
                continue;
            }

            property.GetValue(mutated).ShouldNotBe(
                property.GetValue(stored),
                $"StoredFile.{property.Name} is identical in both samples, so comparing it after WriteTo "
                + "would pass even if WriteTo never wrote it.");
        }
    }

    [Fact]
    public void TheHarness_DetectsAWriteThatForgetsAProperty()
    {
        var mutated = AStoredFileAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(new ForgetfulWriter(), mutated);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertStateWasWritten(mutated, written));

        failure.Message.ShouldContain(nameof(StoredFile.Name));
    }

    private static StoredFile WriteAndReadBack(IStoredFileMapper mapper, StoredFile mutated)
    {
        var tracked = mapper.ToNewRecord(AStoredFileAggregate.FullyPopulated());

        mapper.WriteTo(mutated, tracked);

        return mapper.ToAggregate(tracked);
    }

    private static void AssertStateWasWritten(StoredFile mutated, StoredFile written)
    {
        foreach (var property in StateProperties(_membersWriteToDoesNotOwn))
        {
            property.GetValue(written).ShouldBe(
                property.GetValue(mutated),
                $"StoredFile.{property.Name} did not survive aggregate -> tracked row -> aggregate. "
                + "WriteTo is losing it silently: the insert path carries it, so every round-trip test "
                + "still passes, and every update drops it. Assign it in WriteTo.");
        }
    }

    private static IEnumerable<PropertyInfo> StateProperties(string[] excluded) =>
        typeof(StoredFile).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !excluded.Contains(property.Name, StringComparer.Ordinal))
            .OrderBy(property => property.Name, StringComparer.Ordinal);

    private sealed class ForgetfulWriter : IStoredFileMapper
    {
        private readonly StoredFileMapper _real = new();

        public StoredFile ToAggregate(StoredFileRecord record) => _real.ToAggregate(record);

        public StoredFileRecord ToNewRecord(StoredFile aggregate) => _real.ToNewRecord(aggregate);

        public void WriteTo(StoredFile aggregate, StoredFileRecord record)
        {
            _real.WriteTo(aggregate, record);

            record.Name = AStoredFileAggregate.NameValue;
        }
    }
}
