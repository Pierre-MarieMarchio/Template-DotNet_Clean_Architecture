using System.Reflection;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

/// <summary>
/// The same fidelity guarantee as <see cref="StoredFileMapperFidelityTests"/>, for the path every
/// command actually takes: writing an aggregate onto a row that already exists.
/// </summary>
/// <remarks>
/// A round trip through <c>ToNewRecord</c> only proves the <em>insert</em> path is total. A property
/// added to the aggregate and to <c>ToNewRecord</c> but forgotten in <c>WriteTo</c> passes that check
/// completely: the insert carries it, the read brings it back, and every update silently drops it. This
/// is the mirror, and it is the one that matters — a file is inserted once and updated whenever it is
/// confirmed.
/// </remarks>
public sealed class StoredFileMapperWriteFidelityTests
{
    /// <summary>
    /// Members <c>WriteTo</c> does not write, each for a stated reason. Checked against the type below,
    /// so a rename cannot turn "deliberately not written" into "silently not written".
    /// </summary>
    private static readonly string[] _membersWriteToDoesNotOwn =
    [
        nameof(StoredFile.DomainEvents),

        // PostgreSQL's xmin. A second writer would be a second opinion.
        nameof(StoredFile.Version),

        // The audit interceptor's four values, flowing row -> aggregate and never the other way.
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

    /// <summary>
    /// Non-vacuity: the enumeration has to be finding the properties it claims to check, or the
    /// assertion above would pass over an empty loop.
    /// </summary>
    [Fact]
    public void TheEnumeration_FindsTheStateItIsMeantToCheck()
    {
        var properties = StateProperties(_membersWriteToDoesNotOwn).Select(property => property.Name).ToList();

        properties.ShouldContain(nameof(StoredFile.Id));
        properties.ShouldContain(nameof(StoredFile.OwnerId));
        properties.ShouldContain(nameof(StoredFile.ObjectKey));
        properties.ShouldContain(nameof(StoredFile.Name));
        properties.ShouldContain(nameof(StoredFile.DeclaredMediaType));
        properties.ShouldContain(nameof(StoredFile.Size));
        properties.ShouldContain(nameof(StoredFile.Checksum));
        properties.ShouldContain(nameof(StoredFile.State));
        properties.ShouldContain(nameof(StoredFile.RegisteredAt));
        properties.ShouldContain(nameof(StoredFile.AvailableAt));
    }

    /// <summary>
    /// The sample has to differ from the stored row in every value being compared. Comparing a value
    /// against itself passes for a property <c>WriteTo</c> never touched, which is the whole class of
    /// defect these tests exist for.
    /// </summary>
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

    /// <summary>
    /// Proof that the harness can fail. <see cref="ForgetfulWriter"/> is the real mapper with one
    /// assignment undone — the file's name never reaches its row — and the same walk must reject it. A
    /// green test nobody has seen go red is not evidence of anything.
    /// </summary>
    [Fact]
    public void TheHarness_DetectsAWriteThatForgetsAProperty()
    {
        var mutated = AStoredFileAggregate.DifferentInEveryDomainOwnedValue();

        var written = WriteAndReadBack(new ForgetfulWriter(), mutated);

        var failure = Should.Throw<ShouldAssertException>(
            () => AssertStateWasWritten(mutated, written));

        failure.Message.ShouldContain(nameof(StoredFile.Name));
    }

    // ---- The comparison ------------------------------------------------------------------------

    /// <summary>
    /// Stages <paramref name="mutated"/> onto the row a query would have produced for
    /// <see cref="AStoredFileAggregate.FullyPopulated"/>, then reads that row back as an aggregate. The
    /// row is built by the real mapper, so the fixture cannot disagree with the schema.
    /// </summary>
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

    /// <summary>
    /// The mapper as it would be if somebody forgot one assignment in the update path. Everything else
    /// delegates to the real implementation, so the one difference is the one thing under test.
    /// </summary>
    private sealed class ForgetfulWriter : IStoredFileMapper
    {
        private readonly StoredFileMapper _real = new();

        public StoredFile ToAggregate(StoredFileRecord record) => _real.ToAggregate(record);

        public StoredFileRecord ToNewRecord(StoredFile aggregate) => _real.ToNewRecord(aggregate);

        public void WriteTo(StoredFile aggregate, StoredFileRecord record)
        {
            _real.WriteTo(aggregate, record);

            // The forgotten line: the name never reaches its row.
            record.Name = AStoredFileAggregate.NameValue;
        }
    }
}
