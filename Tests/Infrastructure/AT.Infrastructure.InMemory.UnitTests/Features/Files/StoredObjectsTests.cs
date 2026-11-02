using System.Security.Cryptography;
using System.Text;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using AppTemplate.Infrastructure.InMemory.Features.Files;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

public sealed class StoredObjectsTests
{
    private const string _objectKey = "t0/202601/9f2c1d7a4b6e8f0132547698badcfe10";

    private const string _mediaType = "image/png";

    private static readonly byte[] _content = Encoding.UTF8.GetBytes("the deposited bytes");

    private readonly FixedDateTimeProvider _clock = new();

    [Fact]
    public void Deposit_MeasuresAndDigestsTheBytesItWasGiven()
    {
        var objects = new StoredObjects(_clock);

        var deposited = objects.Deposit(_objectKey, _mediaType, _content);

        deposited.SizeInBytes.ShouldBe(_content.Length);
        deposited.Checksum.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(_content)));
        deposited.MediaType.ShouldBe(_mediaType);
        deposited.DepositedAt.ShouldBe(FixedDateTimeProvider.DefaultInstant);
    }

    [Fact]
    public void Deposit_StampsTheInstantTheInjectedClockReads()
    {
        var objects = new StoredObjects(_clock);
        _clock.Advance(TimeSpan.FromHours(3));

        objects.Deposit(_objectKey, _mediaType, _content)
            .DepositedAt.ShouldBe(FixedDateTimeProvider.DefaultInstant.AddHours(3));
    }

    [Fact]
    public void Deposit_ReplacesWhatWasUnderTheSameKey()
    {
        var objects = new StoredObjects(_clock);
        objects.Deposit(_objectKey, _mediaType, _content);

        objects.Deposit(_objectKey, "application/pdf", [1, 2, 3]);

        objects.Snapshot().ShouldHaveSingleItem().MediaType.ShouldBe("application/pdf");
        objects.Find(_objectKey).ShouldNotBeNull().SizeInBytes.ShouldBe(3);
    }

    [Fact]
    public void Find_AnswersWithNothingForAKeyNobodyDepositedUnder() =>
        new StoredObjects(_clock).Find(_objectKey).ShouldBeNull();

    [Fact]
    public void Snapshot_AnswersInKeyOrder()
    {
        var objects = new StoredObjects(_clock);
        objects.Deposit("t0/202601/c", _mediaType, _content);
        objects.Deposit("t0/202601/a", _mediaType, _content);
        objects.Deposit("t0/202601/b", _mediaType, _content);

        objects.Snapshot().Select(stored => stored.ObjectKey)
            .ShouldBe(["t0/202601/a", "t0/202601/b", "t0/202601/c"]);
    }

    [Fact]
    public void Clear_LeavesNothingForTheNextTestToFind()
    {
        var objects = new StoredObjects(_clock);
        objects.Deposit(_objectKey, _mediaType, _content);

        objects.Clear();

        objects.Snapshot().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://files.in-memory.invalid/t0/202601/a?method=GET&expires=99999999999&signature=00")]
    [InlineData("https://example.invalid/t0/202601/a?method=GET&expires=99999999999&signature=00")]
    [InlineData("not a url at all")]
    public void IsGrantValid_RefusesAnythingItDidNotSign(string url) =>
        new StoredObjects(_clock).IsGrantValid(url, "GET", _clock.UtcNow).ShouldBeFalse();
}
