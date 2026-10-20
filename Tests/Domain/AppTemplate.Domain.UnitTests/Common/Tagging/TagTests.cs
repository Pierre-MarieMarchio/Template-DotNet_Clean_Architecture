using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Domain.Core.Common.Exceptions;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Common.Tagging;

public sealed class TagTests
{
    [Fact]
    public void Create_KeepsAnAlreadyNormalisedValue() => Tag.Create("urgent").Value.ShouldBe("urgent");

    [Fact]
    public void Create_TrimsSurroundingWhitespace() => Tag.Create("  urgent  ").Value.ShouldBe("urgent");

    [Theory]
    [InlineData("URGENT")]
    [InlineData("Urgent")]
    [InlineData("uRgEnT")]
    [InlineData("  URGENT  ")]
    public void Create_LowerCasesTheValue(string input) => Tag.Create(input).Value.ShouldBe("urgent");

    [Fact]
    public void Create_PreservesInnerSpacing() =>
        Tag.Create("  Very Urgent  ").Value.ShouldBe("very urgent");

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_Rejects_ABlankValue(string value)
    {
        var exception = Should.Throw<DomainException>(() => Tag.Create(value));

        exception.Message.ShouldContain("tag");
    }

    [Fact]
    public void Create_Rejects_ANullValue() => Should.Throw<DomainException>(() => Tag.Create(null!));

    [Fact]
    public void Create_Accepts_ExactlyTheMaximumLength() =>
        Tag.Create(new string('a', Tag.MaxLength)).Value.Length.ShouldBe(Tag.MaxLength);

    [Fact]
    public void Create_Rejects_OneCharacterBeyondTheMaximumLength() =>
        Should.Throw<DomainException>(() => Tag.Create(new string('a', Tag.MaxLength + 1)));

    [Fact]
    public void Create_MeasuresTheLengthAfterTrimming() =>
        Tag.Create($"  {new string('a', Tag.MaxLength)}  ").Value.Length.ShouldBe(Tag.MaxLength);

    [Fact]
    public void Equality_IsByValue()
    {
        Tag.Create("urgent").ShouldBe(Tag.Create("urgent"));
        Tag.Create("urgent").ShouldNotBe(Tag.Create("later"));
    }

    [Fact]
    public void Equality_IgnoresCasingAndPaddingBecauseTheValueIsNormalised()
    {
        Tag.Create("URGENT").ShouldBe(Tag.Create("  urgent "));
        Tag.Create("URGENT").GetHashCode().ShouldBe(Tag.Create("urgent").GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsTheNormalisedValue() => Tag.Create("  URGENT ").ToString().ShouldBe("urgent");

    [Fact]
    public void Value_HasNoSetter() =>
        typeof(Tag).GetProperty(nameof(Tag.Value))!.SetMethod.ShouldBeNull();

    [Fact]
    public void TheOnlyWayToBuildATag_IsTheFactory() =>
        typeof(Tag).GetConstructors().ShouldBeEmpty();

    [Fact]
    public void TheMaximumLength_IsFiftyCharacters() => Tag.MaxLength.ShouldBe(50);
}
