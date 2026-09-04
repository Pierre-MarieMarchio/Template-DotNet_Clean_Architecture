using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.ValueObjects;

public sealed class ObjectKeyTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_ProducesAKeyUnderTheReservedPrefix() =>
        ObjectKey.New(_now).Value.ShouldStartWith($"{ObjectKey.UnpartitionedPrefix}/");

    /// <summary>
    /// The prefix is the slot a tenant identifier will occupy. A key minted without one could not be
    /// re-partitioned later without moving the bytes it names, which is the single thing storing the
    /// key rather than deriving it exists to avoid.
    /// </summary>
    [Fact]
    public void New_ProducesAPrefixAThenTimeSliceThenAName()
    {
        string[] segments = ObjectKey.New(_now).Value.Split('/');

        segments.Length.ShouldBe(3);
        segments[0].ShouldBe(ObjectKey.UnpartitionedPrefix);
        segments[1].ShouldBe("202608");
        segments[2].Length.ShouldBe(ObjectKey.NameLength);
    }

    /// <summary>
    /// The whole security argument for a random name: two keys minted in the same instant must share
    /// nothing beyond the slice they are in. A generator that had quietly become deterministic —
    /// seeded, or derived from a clock — would fail here and nowhere else.
    /// </summary>
    [Fact]
    public void New_GivesEveryFileADistinctKey()
    {
        var keys = Enumerable.Range(0, 256).Select(_ => ObjectKey.New(_now).Value).ToList();

        keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(keys.Count);
    }

    /// <summary>
    /// The minting path and the loading path have to agree on what a key is, or a file could be
    /// written and then never loaded again. Routing <c>New</c> through <c>Create</c> is what
    /// guarantees it; this is what would notice if that stopped being true.
    /// </summary>
    [Fact]
    public void Create_Accepts_WhateverNewProduces()
    {
        string minted = ObjectKey.New(_now).Value;

        Should.NotThrow(() => ObjectKey.Create(minted)).Value.ShouldBe(minted);
    }

    #region The time slice the orphan sweep reads

    /// <summary>
    /// What keeps the orphan sweep bounded: an object under a slice can only have been minted by a
    /// row registered in that slice, so a pass lists one prefix instead of the whole store.
    /// </summary>
    [Theory]
    [InlineData(2026, 8, 9, "202608")]
    [InlineData(2026, 1, 1, "202601")]
    [InlineData(2026, 12, 31, "202612")]
    [InlineData(2100, 10, 5, "210010")]
    public void TimeSegmentFor_IsTheCalendarMonthOfTheInstant(int year, int month, int day, string expected) =>
        ObjectKey.TimeSegmentFor(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero))
            .ShouldBe(expected);

    /// <summary>
    /// The most dangerous test in this file. The sweep computes a slice from a row's stored
    /// registration instant and the mint computes it from the same instant, so the two must agree for
    /// every caller — including two hosts running in different offsets. A local interpretation would
    /// put a file registered near a month boundary in one slice on one host and another slice on
    /// another, and the sweep would find live bytes unreferenced and delete them.
    /// </summary>
    [Fact]
    public void TimeSegmentFor_IsComputedInUtc()
    {
        // 00:30 on 1 September at +02:00 is 22:30 on 31 August in UTC: the previous slice.
        ObjectKey.TimeSegmentFor(new DateTimeOffset(2026, 9, 1, 0, 30, 0, TimeSpan.FromHours(2)))
            .ShouldBe("202608");

        // And the other way: 23:30 on 31 August at -03:00 is 02:30 on 1 September in UTC.
        ObjectKey.TimeSegmentFor(new DateTimeOffset(2026, 8, 31, 23, 30, 0, TimeSpan.FromHours(-3)))
            .ShouldBe("202609");
    }

    /// <summary>
    /// The same statement without the arithmetic: one instant is one slice, however it was written.
    /// </summary>
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

    /// <summary>
    /// Parsing is deliberately looser than minting, which produces three segments. The whole reason a
    /// key is stored rather than derived is that the scheme may change; a parser insisting on today's
    /// shape would refuse to load the keys of yesterday's files, which is exactly the "changing the
    /// scheme means moving the bytes" cost being avoided.
    /// </summary>
    [Fact]
    public void Create_Accepts_AKeyWithFewerSegmentsThanNewMints() =>
        Should.NotThrow(() => ObjectKey.Create("t0/0123456789abcdef0123456789abcdef"));

    /// <summary>
    /// A key escaping its own prefix escapes the partition the prefix exists to enforce. The store
    /// resolves keys literally, but the signers and proxies in front of it do not all agree, and the
    /// disagreement is what a traversal exploits.
    /// </summary>
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

    /// <summary>
    /// Every one of these means something to a URL parser, a shell or a filesystem, and a key is
    /// pasted into the path of a signed URL. A character two components disagree about is a
    /// signature that verifies over one string and addresses another.
    /// </summary>
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

    /// <summary>
    /// Half the tooling that will touch a key is case-insensitive, so two keys differing only in
    /// case would be one object to it and two to this system.
    /// </summary>
    [Fact]
    public void Create_Rejects_AnUpperCaseKey() =>
        Should.Throw<DomainException>(() => ObjectKey.Create("t0/202608/ABCDEF"));

    /// <summary>
    /// Not trimmed, unlike every other text value object here: " a/b" and "a/b" name two different
    /// objects to the store, so trimming would hand back a key addressing bytes the caller never
    /// asked for. Refusing is the only safe normalisation, and this is what says so.
    /// </summary>
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

    /// <summary>
    /// A tripwire on the value, not on the mechanism. 128 bits is what makes a leaked key useless as
    /// a starting point for enumerating its neighbours — and the time slice narrows the search space
    /// by nothing, since it is public knowledge that a file was uploaded some month. Shortening the
    /// name is a security decision and has to be a deliberate one rather than a smaller constant.
    /// </summary>
    [Fact]
    public void TheNameSegment_CarriesOneHundredAndTwentyEightBits() => ObjectKey.NameLength.ShouldBe(32);
}
