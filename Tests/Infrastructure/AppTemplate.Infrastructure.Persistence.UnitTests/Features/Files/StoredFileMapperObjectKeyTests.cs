using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

/// <summary>
/// The object key, on its own, because it is the one value on this row whose corruption is not a lost
/// field.
/// <para>
/// A file's bytes are reclaimed by listing the object store and deleting every object no row names.
/// The key column is therefore the <b>only</b> thing vouching for a live file's content, and the
/// reflection walks in <see cref="StoredFileMapperFidelityTests"/> and
/// <see cref="StoredFileMapperWriteFidelityTests"/> already cover it — but they cover it as one property
/// among fifteen, under a message about a value that "came back as its default". These tests say the
/// consequence out loud so that anyone relaxing them can see what they are removing: a mapper that
/// writes a different key does not lose data on this row, it makes the orphan sweep delete a live user's
/// file.
/// </para>
/// </summary>
public sealed class StoredFileMapperObjectKeyTests
{
    private readonly StoredFileMapper _mapper = new();

    /// <summary>
    /// Byte for byte, and compared as a string rather than through <see cref="ObjectKey"/>'s record
    /// equality: the store resolves keys literally, so the guarantee is about the characters that reach
    /// the column, not about two value objects agreeing.
    /// </summary>
    [Fact]
    public void ToNewRecord_WritesTheKeyExactlyAsItWasMinted()
    {
        var aggregate = AStoredFileAggregate.FullyPopulated();

        string written = _mapper.ToNewRecord(aggregate).ObjectKey;

        written.ShouldBe(
            AStoredFileAggregate.ObjectKeyValue,
            "the row must name the object the upload grant was minted against. A key normalised, "
            + "re-cased, trimmed or re-minted here names an object nobody wrote, and the orphan sweep "
            + "then deletes the bytes the user did upload.");
    }

    [Fact]
    public void TheRoundTrip_ReturnsTheSameKey()
    {
        var original = AStoredFileAggregate.FullyPopulated();

        var roundTripped = _mapper.ToAggregate(_mapper.ToNewRecord(original));

        roundTripped.ObjectKey.Value.ShouldBe(original.ObjectKey.Value);
    }

    [Fact]
    public void WriteTo_PutsTheKeyOnAnAlreadyStoredRow()
    {
        var stored = _mapper.ToNewRecord(AStoredFileAggregate.FullyPopulated());

        _mapper.WriteTo(AStoredFileAggregate.DifferentInEveryDomainOwnedValue(), stored);

        stored.ObjectKey.ShouldBe(
            AStoredFileAggregate.OtherObjectKeyValue,
            "the update path has to assert the key as well; a column left out of WriteTo is a column an "
            + "update can never correct.");
    }

    /// <summary>
    /// A key from an older scheme — two segments, no time slice — such as a row written before the
    /// current mint existed. It has to load, because the whole reason a key is stored rather than
    /// derived is that the scheme may change, and a file whose key no longer parses is a file nobody can
    /// read or delete.
    /// </summary>
    [Theory]
    [InlineData("t0/legacy-object_1.bin")]
    [InlineData("t0/202606/0123456789abcdef0123456789abcdef")]
    public void AKeyFromAnyScheme_SurvivesTheRoundTrip(string key)
    {
        var record = _mapper.ToNewRecord(AStoredFileAggregate.FullyPopulated());
        record.ObjectKey = key;

        _mapper.ToAggregate(record).ObjectKey.Value.ShouldBe(key);
    }

    /// <summary>
    /// The longest key the domain permits, which is what the column has to hold. It is checked here as
    /// well as in the configuration because the two failures are different: a column too short is a
    /// refused write, and a mapper that shortened the value would be a silent one.
    /// </summary>
    [Fact]
    public void AKeyAtTheDomainsCeiling_SurvivesTheRoundTrip()
    {
        string longest = ObjectKey.UnpartitionedPrefix
            + "/"
            + new string('a', ObjectKey.MaxLength - ObjectKey.UnpartitionedPrefix.Length - 1);

        longest.Length.ShouldBe(ObjectKey.MaxLength, "the sample has to actually sit at the ceiling.");

        var record = _mapper.ToNewRecord(AStoredFileAggregate.FullyPopulated());
        record.ObjectKey = longest;

        _mapper.ToAggregate(record).ObjectKey.Value.ShouldBe(longest);
    }
}
