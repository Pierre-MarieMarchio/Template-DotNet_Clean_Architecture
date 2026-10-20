using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Model.ValueObjects;

public sealed class StoredFileNameTests
{
    [Fact]
    public void Create_KeepsAnOrdinaryName() =>
        StoredFileName.Create("quarterly-report.pdf").Value.ShouldBe("quarterly-report.pdf");

    [Fact]
    public void Create_TrimsSurroundingWhitespace() =>
        StoredFileName.Create("  report.pdf  ").Value.ShouldBe("report.pdf");

    [Fact]
    public void Create_PreservesCase() => StoredFileName.Create("Report.PDF").Value.ShouldBe("Report.PDF");

    [Fact]
    public void Create_PreservesInnerSpacing() =>
        StoredFileName.Create("  my holiday photo.jpg ").Value.ShouldBe("my holiday photo.jpg");

    [Fact]
    public void Create_PreservesNonAsciiCharacters() =>
        StoredFileName.Create("relevé de compte — août.pdf").Value.ShouldBe("relevé de compte — août.pdf");

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config\\sam")]
    [InlineData("/etc/passwd")]
    [InlineData("subdir/report.pdf")]
    [InlineData("subdir\\report.pdf")]
    [InlineData("C:\\report.pdf")]
    public void Create_Rejects_ANameCarryingAPathSeparator(string value)
    {
        var exception = Should.Throw<DomainException>(() => StoredFileName.Create(value));

        exception.Message.ShouldContain("path separator");
    }

    [Fact]
    public void Create_Accepts_DotsInsideAName() =>
        StoredFileName.Create("archive..2026.tar.gz").Value.ShouldBe("archive..2026.tar.gz");

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData(" .. ")]
    public void Create_Rejects_ANameThatIsOnlyDots(string value)
    {
        var exception = Should.Throw<DomainException>(() => StoredFileName.Create(value));

        exception.Message.ShouldContain("dots");
    }

    [Fact]
    public void Create_Accepts_ALeadingDot() => StoredFileName.Create(".gitignore").Value.ShouldBe(".gitignore");

    [Theory]
    [InlineData("report\0.pdf")]
    [InlineData("report\n.pdf")]
    [InlineData("report\r\nX-Injected 1.pdf")]
    [InlineData("report\t.pdf")]
    [InlineData("report\u001b[0m.pdf")]
    public void Create_Rejects_AControlCharacter(string value)
    {
        var exception = Should.Throw<DomainException>(() => StoredFileName.Create(value));

        exception.Message.ShouldContain("control character");
    }

    [Theory]
    [InlineData("report:final.pdf")]
    [InlineData("report*.pdf")]
    [InlineData("report?.pdf")]
    [InlineData("report\".pdf")]
    [InlineData("report<1>.pdf")]
    [InlineData("report|1.pdf")]
    public void Create_Rejects_ACharacterNoCommonPlatformCanSave(string value) =>
        Should.Throw<DomainException>(() => StoredFileName.Create(value));

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL.txt")]
    [InlineData("nul.TXT")]
    [InlineData("COM1.pdf")]
    [InlineData("lpt9.tar.gz")]
    [InlineData("AUX")]
    [InlineData("PRN.doc")]
    public void Create_Rejects_AReservedDeviceName(string value)
    {
        var exception = Should.Throw<DomainException>(() => StoredFileName.Create(value));

        exception.Message.ShouldContain("reserved device name");
    }

    [Theory]
    [InlineData("console.log")]
    [InlineData("communication.txt")]
    [InlineData("nullable.cs")]
    [InlineData("auxiliary.dat")]
    public void Create_Accepts_ANameThatMerelyBeginsLikeADeviceName(string value) =>
        Should.NotThrow(() => StoredFileName.Create(value));

    [Theory]
    [InlineData("report.pdf.")]
    [InlineData("report.pdf ")]
    [InlineData("report.pdf. . ")]
    public void Create_StripsTrailingDotsAndSpaces(string value) =>
        StoredFileName.Create(value).Value.ShouldBe("report.pdf");

    [Fact]
    public void Equality_IgnoresPaddingBecauseTheValueIsNormalised() =>
        StoredFileName.Create("  report.pdf. ").ShouldBe(StoredFileName.Create("report.pdf"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Create_Rejects_ABlankValue(string value) =>
        Should.Throw<DomainException>(() => StoredFileName.Create(value));

    [Fact]
    public void Create_Rejects_ANullValue() =>
        Should.Throw<DomainException>(() => StoredFileName.Create(null!));

    [Fact]
    public void Create_Accepts_ExactlyTheMaximumLength() =>
        StoredFileName.Create(new string('a', StoredFileName.MaxLength))
            .Value.Length.ShouldBe(StoredFileName.MaxLength);

    [Fact]
    public void Create_Rejects_OneCharacterBeyondTheMaximumLength() =>
        Should.Throw<DomainException>(
            () => StoredFileName.Create(new string('a', StoredFileName.MaxLength + 1)));

    [Fact]
    public void Create_MeasuresTheLengthAfterNormalising() =>
        StoredFileName.Create($" {new string('a', StoredFileName.MaxLength)} ")
            .Value.Length.ShouldBe(StoredFileName.MaxLength);

    [Fact]
    public void TheMaximumLength_IsOnePathComponent() => StoredFileName.MaxLength.ShouldBe(255);

    [Fact]
    public void ToString_ReturnsTheNormalisedValue() =>
        StoredFileName.Create(" report.pdf ").ToString().ShouldBe("report.pdf");

    [Fact]
    public void TheOnlyWayToBuildAFileName_IsTheFactory() =>
        typeof(StoredFileName).GetConstructors().ShouldBeEmpty();
}
