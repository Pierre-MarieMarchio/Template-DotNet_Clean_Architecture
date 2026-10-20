using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Model.ValueObjects;

public sealed class DeclaredMediaTypeTests
{
    [Theory]
    [InlineData("image/png")]
    [InlineData("audio/mpeg")]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("text/plain")]
    public void Create_Accepts_AWellFormedType(string value) =>
        DeclaredMediaType.Create(value).Value.ShouldBe(value);

    [Fact]
    public void Create_ExposesBothHalves()
    {
        var mediaType = DeclaredMediaType.Create("image/png");

        mediaType.Type.ShouldBe("image");
        mediaType.Subtype.ShouldBe("png");
    }

    [Theory]
    [InlineData("IMAGE/PNG")]
    [InlineData("Image/Png")]
    [InlineData("  image/PNG  ")]
    public void Create_NormalisesCaseAndPadding(string value) =>
        DeclaredMediaType.Create(value).Value.ShouldBe("image/png");

    [Fact]
    public void Equality_IgnoresCasingBecauseTheValueIsNormalised()
    {
        DeclaredMediaType.Create("IMAGE/PNG").ShouldBe(DeclaredMediaType.Create("image/png"));
        DeclaredMediaType.Create("image/png").ShouldNotBe(DeclaredMediaType.Create("image/jpeg"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_ABlankValue(string value) =>
        Should.Throw<DomainException>(() => DeclaredMediaType.Create(value));

    [Fact]
    public void Create_Rejects_ANullValue() =>
        Should.Throw<DomainException>(() => DeclaredMediaType.Create(null!));

    [Theory]
    [InlineData("image")]
    [InlineData("image/png/extra")]
    public void Create_Rejects_AValueThatIsNotTypeSlashSubtype(string value)
    {
        var exception = Should.Throw<DomainException>(() => DeclaredMediaType.Create(value));

        exception.Message.ShouldContain("type/subtype");
    }

    [Theory]
    [InlineData("/png")]
    [InlineData("image/")]
    public void Create_Rejects_AMissingHalf(string value)
    {
        var exception = Should.Throw<DomainException>(() => DeclaredMediaType.Create(value));

        exception.Message.ShouldContain("both a type and a subtype");
    }

    [Theory]
    [InlineData("*/*")]
    [InlineData("image/*")]
    [InlineData("*/png")]
    public void Create_Rejects_AWildcard(string value)
    {
        var exception = Should.Throw<DomainException>(() => DeclaredMediaType.Create(value));

        exception.Message.ShouldContain("wildcard");
    }

    [Theory]
    [InlineData("image/png; charset=utf-8")]
    [InlineData("text/plain;charset=utf-8")]
    [InlineData("multipart/form-data; boundary=x")]
    public void Create_Rejects_AParameter(string value) =>
        Should.Throw<DomainException>(() => DeclaredMediaType.Create(value));

    [Theory]
    [InlineData("image/p\0ng")]
    [InlineData("image/p ng")]
    [InlineData("image\\png")]
    [InlineData("image/p\nng")]
    [InlineData("<script>/png")]
    public void Create_Rejects_ANonTokenCharacter(string value) =>
        Should.Throw<DomainException>(() => DeclaredMediaType.Create(value));

    [Fact]
    public void Create_Accepts_EveryTokenCharacterRfc9110Allows() =>
        Should.NotThrow(() => DeclaredMediaType.Create("x!#$%&'+-.^_`|~/y0123456789"));

    [Fact]
    public void Create_Accepts_BothHalvesAtTheirMaximumLength()
    {
        string token = new('a', DeclaredMediaType.MaxTokenLength);

        DeclaredMediaType.Create($"{token}/{token}").Value.Length.ShouldBe(DeclaredMediaType.MaxLength);
    }

    [Fact]
    public void Create_Rejects_AHalfOneCharacterTooLong()
    {
        string token = new('a', DeclaredMediaType.MaxTokenLength + 1);

        Should.Throw<DomainException>(() => DeclaredMediaType.Create($"{token}/png"));
        Should.Throw<DomainException>(() => DeclaredMediaType.Create($"image/{token}"));
    }

    [Fact]
    public void TheMaximumTokenLength_IsWhatIanaRegisters() =>
        DeclaredMediaType.MaxTokenLength.ShouldBe(127);

    [Fact]
    public void ToString_ReturnsTheNormalisedValue() =>
        DeclaredMediaType.Create(" IMAGE/PNG ").ToString().ShouldBe("image/png");

    [Fact]
    public void TheOnlyWayToBuildAMediaType_IsTheFactory() =>
        typeof(DeclaredMediaType).GetConstructors().ShouldBeEmpty();
}
