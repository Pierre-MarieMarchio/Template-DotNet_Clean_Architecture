using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.TodoLists.ValueObjects;

public sealed class TagTests
{
    [Fact]
    public void Create_KeepsAnAlreadyNormalisedValue() => Tag.Create("urgent").Value.ShouldBe("urgent");

    [Fact]
    public void Create_TrimsSurroundingWhitespace() => Tag.Create("  urgent  ").Value.ShouldBe("urgent");

    /// <summary>
    /// Lower-casing is what makes "Urgent", "urgent " and "URGENT" one tag. Dropping it
    /// makes filtering by a tag depend on how it was typed.
    /// </summary>
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

    /// <summary>
    /// Normalisation and equality together are what make de-duplication work inside an
    /// item: two differently typed spellings must land on the same value.
    /// </summary>
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

    /// <summary>
    /// A tripwire on the value of the limit, not on the mechanism: every other length test above
    /// reads the constant and is therefore blind to a change in the constant itself. The column is
    /// sized from this number, so widening it is a schema decision and has to be a deliberate one.
    /// </summary>
    [Fact]
    public void TheMaximumLength_IsFiftyCharacters() => Tag.MaxLength.ShouldBe(50);
}
