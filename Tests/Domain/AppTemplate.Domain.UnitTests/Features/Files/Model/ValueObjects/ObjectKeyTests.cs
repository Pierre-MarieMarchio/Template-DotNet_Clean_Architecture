using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Model.ValueObjects;

public sealed class ObjectKeyTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_ProducesAKeyUnderTheReservedPrefix() =>
        ObjectKey.New(_now).Value.ShouldStartWith($"{ObjectKey.UnpartitionedPrefix}/");

    [Fact]
    public void New_ProducesAPrefixAThenTimeSliceThenAName()
    {
        string[] segments = ObjectKey.New(_now).Value.Split('/');

        segments.Length.ShouldBe(3);
        segments[0].ShouldBe(ObjectKey.UnpartitionedPrefix);
        segments[1].ShouldBe("202608");
        segments[2].Length.ShouldBe(ObjectKey.NameLength);
    }

    [Fact]
    public void New_GivesEveryFileADistinctKey()
    {
        var keys = Enumerable.Range(0, 256).Select(_ => ObjectKey.New(_now).Value).ToList();

        keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(keys.Count);
    }

    [Fact]
    public void Create_Accepts_WhateverNewProduces()
    {
        string minted = ObjectKey.New(_now).Value;

        Should.NotThrow(() => ObjectKey.Create(minted)).Value.ShouldBe(minted);
    }

    #region The time slice the orphan sweep reads

    [Theory]
    [InlineData(2026, 8, 9, "202608")]
    [InlineData(2026, 1, 1, "202601")]
    [InlineData(2026, 12, 31, "202612")]
    [InlineData(2100, 10, 5, "210010")]
    public void TimeSegmentFor_IsTheCalendarMonthOfTheInstant(int year, int month, int day, string expected) =>
        ObjectKey.TimeSegmentFor(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero))
            .ShouldBe(expected);

    [Fact]
    public void TimeSegmentFor_IsComputedInUtc()
    {
        ObjectKey.TimeSegmentFor(new DateTimeOffset(2026, 9, 1, 0, 30, 0, TimeSpan.FromHours(2)))
            .ShouldBe("202608");

        ObjectKey.TimeSegmentFor(new DateTimeOffset(2026, 8, 31, 23, 30, 0, TimeSpan.FromHours(-3)))
            .ShouldBe("202609");
    }

    [Fact]
    public void TimeSegmentFor_GivesOneAnswerPerInstantWhateverTheOffset()
    {
        var utc = new DateTimeOffset(2026, 9, 1, 0, 30, 0, TimeSpan.FromHours(2));

        ObjectKey.TimeSegmentFor(utc).ShouldBe(ObjectKey.TimeSegmentFor(utc.ToUniversalTime()));
        ObjectKey.TimeSegmentFor(utc).ShouldBe(ObjectKey.TimeSegmentFor(utc.ToOffset(TimeSpan.FromHours(-7))));
    }

    [Fact]
    public void New_MintsTheKeyIntoTheSliceOfTheInstantItWasGiven() =>
        ObjectKey.New(_now).Value.Split('/')[1].ShouldBe(ObjectKey.TimeSegmentFor(_now));

    #endregion

    #region Parsing

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_ABlankValue(string value) =>
        Should.Throw<DomainException>(() => ObjectKey.Create(value));

    [Fact]
    public void Create_Rejects_ANullValue() => Should.Throw<DomainException>(() => ObjectKey.Create(null!));

    [Fact]
    public void Create_Accepts_AKeyWithFewerSegmentsThanNewMints() =>
        Should.NotThrow(() => ObjectKey.Create("t0/0123456789abcdef0123456789abcdef"));

    [Theory]
    [InlineData("t0/../other/object")]
    [InlineData("../t0/202608/object")]
    [InlineData("t0/./object")]
    [InlineData("t0/202608/../../b")]
    public void Create_Rejects_ATraversalSegment(string value)
    {
        var exception = Should.Throw<DomainException>(() => ObjectKey.Create(value));

        exception.Message.ShouldContain("segment");
    }

    [Theory]
    [InlineData("/t0/202608/object")]
    [InlineData("t0/202608/object/")]
    [InlineData("t0//object")]
    public void Create_Rejects_AnEmptySegment(string value) =>
        Should.Throw<DomainException>(() => ObjectKey.Create(value));

    [Fact]
    public void Create_Rejects_AKeyWithNoPrefixSegment()
    {
        var exception = Should.Throw<DomainException>(() => ObjectKey.Create("object"));

        exception.Message.ShouldContain("prefix");
    }

    [Theory]
    [InlineData("t0/202608/obj ect")]
    [InlineData("t0/202608/obj%2fect")]
    [InlineData("t0/202608/obj?ect")]
    [InlineData("t0/202608/obj#ect")]
    [InlineData("t0/202608/obj\\ect")]
    [InlineData("t0/202608/obj\nect")]
    [InlineData("t0/202608/obj\0ect")]
    public void Create_Rejects_ACharacterOutsideTheAllowedSet(string value) =>
        Should.Throw<DomainException>(() => ObjectKey.Create(value));

    [Fact]
    public void Create_Rejects_AnUpperCaseKey() =>
        Should.Throw<DomainException>(() => ObjectKey.Create("t0/202608/ABCDEF"));

    [Fact]
    public void Create_DoesNotTrim_ButRefusesSurroundingWhitespace() =>
        Should.Throw<DomainException>(() => ObjectKey.Create(" t0/202608/object "));

    [Fact]
    public void Create_Accepts_ExactlyTheMaximumLength()
    {
        string value = $"t0/{new string('a', ObjectKey.MaxLength - 3)}";

        ObjectKey.Create(value).Value.Length.ShouldBe(ObjectKey.MaxLength);
    }

    [Fact]
    public void Create_Rejects_OneCharacterBeyondTheMaximumLength() =>
        Should.Throw<DomainException>(() => ObjectKey.Create($"t0/{new string('a', ObjectKey.MaxLength - 2)}"));

    #endregion

    [Fact]
    public void Equality_IsByValue()
    {
        ObjectKey.Create("t0/202608/abc").ShouldBe(ObjectKey.Create("t0/202608/abc"));
        ObjectKey.Create("t0/202608/abc").ShouldNotBe(ObjectKey.Create("t0/202608/abd"));
    }

    [Fact]
    public void ToString_ReturnsTheKey() =>
        ObjectKey.Create("t0/202608/abc").ToString().ShouldBe("t0/202608/abc");

    [Fact]
    public void TheOnlyWayToBuildAnObjectKey_IsAFactory() =>
        typeof(ObjectKey).GetConstructors().ShouldBeEmpty();

    [Fact]
    public void TheNameSegment_CarriesOneHundredAndTwentyEightBits() => ObjectKey.NameLength.ShouldBe(32);
}
