using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

public sealed class StoredFileMapperObjectKeyTests
{
    private readonly StoredFileMapper _mapper = new();

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

    [Theory]
    [InlineData("t0/legacy-object_1.bin")]
    [InlineData("t0/202606/0123456789abcdef0123456789abcdef")]
    public void AKeyFromAnyScheme_SurvivesTheRoundTrip(string key)
    {
        var record = _mapper.ToNewRecord(AStoredFileAggregate.FullyPopulated());
        record.ObjectKey = key;

        _mapper.ToAggregate(record).ObjectKey.Value.ShouldBe(key);
    }

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
