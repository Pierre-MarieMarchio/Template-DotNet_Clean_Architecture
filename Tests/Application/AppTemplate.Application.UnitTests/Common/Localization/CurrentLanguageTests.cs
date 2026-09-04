using AppTemplate.Application.Common.Localization;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Localization;

/// <summary>
/// The tag carrier that stands in for <c>CultureInfo.CurrentUICulture</c>, which this repository
/// builds without. What matters is that a malformed tag can never take hold, and that a regional
/// reader is offered their language before the fallback.
/// </summary>
public sealed class CurrentLanguageTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("fr-CA")]
    [InlineData("zh-Hans")]
    [InlineData("de-DE")]
    public void AWellFormedTag_IsAccepted(string tag) => CurrentLanguage.IsWellFormed(tag).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("*")]
    [InlineData("e")]
    [InlineData("english please")]
    [InlineData("fr_CA")]
    [InlineData("fr-")]
    [InlineData("../../etc/passwd")]
    public void AMalformedTag_IsRejected(string? tag) => CurrentLanguage.IsWellFormed(tag).ShouldBeFalse();

    /// <summary>
    /// A tag arrives in a header a caller controls, so its length is a caller's choice. The cap is
    /// what keeps this from being a way to make the process work.
    /// </summary>
    [Fact]
    public void AnAbsurdlyLongTag_IsRejected() =>
        CurrentLanguage.IsWellFormed("en-" + new string('a', 200)).ShouldBeFalse();

    [Theory]
    [InlineData("fr", new[] { "fr" })]
    [InlineData("fr-CA", new[] { "fr-CA", "fr" })]
    [InlineData("zh-Hans-CN", new[] { "zh-Hans-CN", "zh-Hans", "zh" })]
    public void TheCandidates_NarrowFromTheTagToItsLanguage(string tag, string[] expected) =>
        CurrentLanguage.Candidates(tag).ToList().ShouldBe(expected);

    /// <summary>
    /// A tag the caller sent is never taken on trust: a malformed one leaves the ambient value unset
    /// rather than storing something a renderer would then try to match.
    /// </summary>
    [Fact]
    public void SettingAMalformedTag_LeavesNothingBehind()
    {
        CurrentLanguage.Tag = "fr-CA";
        CurrentLanguage.Tag.ShouldBe("fr-CA");

        CurrentLanguage.Tag = "not a tag";
        CurrentLanguage.Tag.ShouldBeNull();

        CurrentLanguage.Tag = null;
    }

    [Fact]
    public void WithNoTagSet_CurrentIsTheHostsDefault()
    {
        CurrentLanguage.Tag = null;

        CurrentLanguage.Current.ShouldBe(CurrentLanguage.Default);
    }

    /// <summary>
    /// The host's default is the one value a bad configuration must not be able to install: unlike a
    /// request's tag, nothing downstream would ever correct it.
    /// </summary>
    [Fact]
    public void SettingAMalformedDefault_Throws()
    {
        string original = CurrentLanguage.Default;

        try
        {
            Should.Throw<ArgumentException>(() => CurrentLanguage.Default = "not a tag");
            CurrentLanguage.Default.ShouldBe(original);
        }
        finally
        {
            CurrentLanguage.Default = original;
        }
    }

    [Fact]
    public void TheFallbackTag_IsAWellFormedTag() =>
        CurrentLanguage.IsWellFormed(CurrentLanguage.FallbackTag).ShouldBeTrue();

}
