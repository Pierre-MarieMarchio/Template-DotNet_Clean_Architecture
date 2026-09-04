using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.ValueObjects;

public sealed class StoredFileNameTests
{
    [Fact]
    public void Create_KeepsAnOrdinaryName() =>
        StoredFileName.Create("quarterly-report.pdf").Value.ShouldBe("quarterly-report.pdf");

    [Fact]
    public void Create_TrimsSurroundingWhitespace() =>
        StoredFileName.Create("  report.pdf  ").Value.ShouldBe("report.pdf");

    /// <summary>
    /// Case is preserved: this is what the user typed and what they will be shown. Unlike a tag or a
    /// media type, two spellings of a file name are two names, not one.
    /// </summary>
    [Fact]
    public void Create_PreservesCase() => StoredFileName.Create("Report.PDF").Value.ShouldBe("Report.PDF");

    [Fact]
    public void Create_PreservesInnerSpacing() =>
        StoredFileName.Create("  my holiday photo.jpg ").Value.ShouldBe("my holiday photo.jpg");

    [Fact]
    public void Create_PreservesNonAsciiCharacters() =>
        StoredFileName.Create("relevé de compte — août.pdf").Value.ShouldBe("relevé de compte — août.pdf");

    #region Path traversal

    /// <summary>
    /// The rule that matters most, and the only one that would be load-bearing if this value ever
    /// reached a path. Without a separator no arrangement of dots leaves a directory, so refusing
    /// both separators is what makes traversal impossible rather than merely unlikely.
    /// </summary>
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

    /// <summary>
    /// The converse, and the reason the rule above names separators rather than dots: two dots
    /// between two ordinary characters traverse nothing, and rejecting them would refuse a
    /// legitimate name to prevent an attack that cannot be mounted without a separator.
    /// </summary>
    [Fact]
    public void Create_Accepts_DotsInsideAName() =>
        StoredFileName.Create("archive..2026.tar.gz").Value.ShouldBe("archive..2026.tar.gz");

    /// <summary>
    /// Both are names for a directory rather than for a file, and both survive the separator rule.
    /// Trailing-dot normalisation is what removes them, which is why neither has a rule of its own.
    /// </summary>
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

    /// <summary>
    /// A leading dot is a hidden file, not a traversal, and it is an entirely ordinary name.
    /// </summary>
    [Fact]
    public void Create_Accepts_ALeadingDot() => StoredFileName.Create(".gitignore").Value.ShouldBe(".gitignore");

    #endregion

    #region Characters that break whatever saves the file

    /// <summary>
    /// The NUL byte truncates the name in anything that hands it to a C API, and a newline lets a
    /// name inject a second header line into the <c>Content-Disposition</c> it is written to — which
    /// is header injection through a filename, and the reason control characters are refused rather
    /// than stripped.
    /// </summary>
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

    /// <summary>
    /// Windows resolves these before it looks at the directory. Saving as <c>NUL.txt</c> writes to
    /// the null device and the user's file is gone with no error anywhere, so the extension has to
    /// be ignored when matching — which is what makes this more than a list of four words.
    /// </summary>
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

    /// <summary>
    /// The match is on the stem, not on a prefix: refusing every name that merely starts with those
    /// three letters would refuse "console.log" and "communication.txt".
    /// </summary>
    [Theory]
    [InlineData("console.log")]
    [InlineData("communication.txt")]
    [InlineData("nullable.cs")]
    [InlineData("auxiliary.dat")]
    public void Create_Accepts_ANameThatMerelyBeginsLikeADeviceName(string value) =>
        Should.NotThrow(() => StoredFileName.Create(value));

    #endregion

    #region Normalisation

    /// <summary>
    /// Windows strips a trailing dot or space when it creates the file, silently. Two names that
    /// would land on the user's disk as one file have to be one name here too, or this system
    /// believes it is holding two files that the user can only ever save as one.
    /// </summary>
    [Theory]
    [InlineData("report.pdf.")]
    [InlineData("report.pdf ")]
    [InlineData("report.pdf. . ")]
    public void Create_StripsTrailingDotsAndSpaces(string value) =>
        StoredFileName.Create(value).Value.ShouldBe("report.pdf");

    [Fact]
    public void Equality_IgnoresPaddingBecauseTheValueIsNormalised() =>
        StoredFileName.Create("  report.pdf. ").ShouldBe(StoredFileName.Create("report.pdf"));

    #endregion

    #region Bounds

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

    /// <summary>
    /// A tripwire on the value, not on the mechanism: 255 is what one path component holds on the
    /// filesystems a download lands on, and a longer name is truncated by whatever saves it rather
    /// than refused.
    /// </summary>
    [Fact]
    public void TheMaximumLength_IsOnePathComponent() => StoredFileName.MaxLength.ShouldBe(255);

    #endregion

    [Fact]
    public void ToString_ReturnsTheNormalisedValue() =>
        StoredFileName.Create(" report.pdf ").ToString().ShouldBe("report.pdf");

    [Fact]
    public void TheOnlyWayToBuildAFileName_IsTheFactory() =>
        typeof(StoredFileName).GetConstructors().ShouldBeEmpty();
}
