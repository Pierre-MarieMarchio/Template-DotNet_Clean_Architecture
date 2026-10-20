using AppTemplate.Application.Core.Common.Localization;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Localization;

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

    [Fact]
    public void AnAbsurdlyLongTag_IsRejected() =>
        CurrentLanguage.IsWellFormed("en-" + new string('a', 200)).ShouldBeFalse();

    [Theory]
    [InlineData("fr", new[] { "fr" })]
    [InlineData("fr-CA", new[] { "fr-CA", "fr" })]
    [InlineData("zh-Hans-CN", new[] { "zh-Hans-CN", "zh-Hans", "zh" })]
    public void TheCandidates_NarrowFromTheTagToItsLanguage(string tag, string[] expected) =>
        CurrentLanguage.Candidates(tag).ToList().ShouldBe(expected);

    [Fact]
    public void ANullTag_IsRefusedByTheCallItself() =>
        Should.Throw<ArgumentNullException>(() => CurrentLanguage.Candidates(null!));

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
