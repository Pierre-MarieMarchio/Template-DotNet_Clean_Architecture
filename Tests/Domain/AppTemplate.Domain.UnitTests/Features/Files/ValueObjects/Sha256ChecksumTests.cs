using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.ValueObjects;

public sealed class Sha256ChecksumTests
{
    /// <summary>The SHA-256 of the empty input — a real digest rather than 64 arbitrary characters.</summary>
    private const string _emptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void Create_KeepsAnAlreadyNormalisedDigest() =>
        Sha256Checksum.Create(_emptyDigest).Value.ShouldBe(_emptyDigest);

    /// <summary>
    /// The two ends of the comparison are not under the same control: the client computes one digest
    /// and the object store reports the other, and nothing makes them agree on casing. Normalising is
    /// what makes the confirmation check correct whichever casing arrives — without it, an upload
    /// would fail confirmation for a reason no message could explain.
    /// </summary>
    [Fact]
    public void Create_LowerCasesTheDigest() =>
        Sha256Checksum.Create(_emptyDigest.ToUpperInvariant()).Value.ShouldBe(_emptyDigest);

    [Fact]
    public void Equality_IgnoresCasingBecauseTheValueIsNormalised()
    {
        Sha256Checksum.Create(_emptyDigest.ToUpperInvariant()).ShouldBe(Sha256Checksum.Create(_emptyDigest));
        Sha256Checksum.Create(_emptyDigest).ShouldNotBe(Sha256Checksum.Create(new string('a', 64)));
    }

    [Fact]
    public void Create_TrimsSurroundingWhitespace() =>
        Sha256Checksum.Create($"  {_emptyDigest}  ").Value.ShouldBe(_emptyDigest);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_ABlankValue(string value) =>
        Should.Throw<DomainException>(() => Sha256Checksum.Create(value));

    [Fact]
    public void Create_Rejects_ANullValue() =>
        Should.Throw<DomainException>(() => Sha256Checksum.Create(null!));

    /// <summary>
    /// The length is the algorithm. A digest of another length is not a checksum to investigate, it
    /// is a value that can never compare equal to what the store reports — which would present as
    /// every file failing confirmation, permanently, for no stated reason.
    /// </summary>
    [Theory]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(32)]
    [InlineData(40)]
    public void Create_Rejects_ADigestOfTheWrongLength(int length)
    {
        var exception = Should.Throw<DomainException>(() => Sha256Checksum.Create(new string('a', length)));

        exception.Message.ShouldContain("exactly");
    }

    [Theory]
    [InlineData("g3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85 ")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85-")]
    public void Create_Rejects_ANonHexadecimalCharacter(string value) =>
        Should.Throw<DomainException>(() => Sha256Checksum.Create(value));

    [Fact]
    public void ToString_ReturnsTheNormalisedDigest() =>
        Sha256Checksum.Create(_emptyDigest.ToUpperInvariant()).ToString().ShouldBe(_emptyDigest);

    [Fact]
    public void TheOnlyWayToBuildAChecksum_IsTheFactory() =>
        typeof(Sha256Checksum).GetConstructors().ShouldBeEmpty();

    /// <summary>A tripwire on the value: 256 bits at four bits per hexadecimal character.</summary>
    [Fact]
    public void TheLength_IsThatOfASha256Digest() => Sha256Checksum.Length.ShouldBe(64);
}
