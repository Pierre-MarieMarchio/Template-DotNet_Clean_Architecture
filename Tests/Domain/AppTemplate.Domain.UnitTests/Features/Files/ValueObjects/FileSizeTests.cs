using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.ValueObjects;

public sealed class FileSizeTests
{
    [Fact]
    public void Create_KeepsTheByteCount() => FileSize.Create(4096).Bytes.ShouldBe(4096);

    /// <summary>
    /// A zero-byte object and an upload that never happened are the same thing to the object store.
    /// Registering before depositing exists precisely to tell those two apart, so accepting zero
    /// would create a file that can never be confirmed and never be distinguished from an abandoned
    /// one.
    /// </summary>
    [Fact]
    public void Create_Rejects_AnEmptyFile()
    {
        var exception = Should.Throw<DomainException>(() => FileSize.Create(0));

        exception.Message.ShouldContain("at least");
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Create_Rejects_ANegativeSize(long bytes) =>
        Should.Throw<DomainException>(() => FileSize.Create(bytes));

    [Fact]
    public void Create_Accepts_ASingleByte() => FileSize.Create(1).Bytes.ShouldBe(1);

    [Fact]
    public void Create_Accepts_ExactlyTheMaximum() =>
        FileSize.Create(FileSize.MaxBytes).Bytes.ShouldBe(FileSize.MaxBytes);

    /// <summary>
    /// One byte past the ceiling is a file that physically cannot be deposited: the feature issues
    /// one signed URL and therefore one part, and a single-part upload is capped by the protocol.
    /// Accepting the registration would promise a deposit nobody can make.
    /// </summary>
    [Fact]
    public void Create_Rejects_OneByteBeyondTheMaximum() =>
        Should.Throw<DomainException>(() => FileSize.Create(FileSize.MaxBytes + 1));

    [Fact]
    public void Create_Rejects_TheLargestPossibleLong() =>
        Should.Throw<DomainException>(() => FileSize.Create(long.MaxValue));

    /// <summary>
    /// A tripwire on the value, not on the mechanism. 5 GiB is the protocol's own single-part
    /// ceiling; raising it means adding a multipart upload session, not editing a constant.
    /// </summary>
    [Fact]
    public void TheMaximum_IsTheSinglePartCeiling() => FileSize.MaxBytes.ShouldBe(5L * 1024 * 1024 * 1024);

    /// <summary>
    /// Equality by value is what <c>ConfirmAvailable</c> compares the declared size against the
    /// observed one with; comparison by reference would make every confirmation fail.
    /// </summary>
    [Fact]
    public void Equality_IsByValue()
    {
        FileSize.Create(4096).ShouldBe(FileSize.Create(4096));
        FileSize.Create(4096).ShouldNotBe(FileSize.Create(4097));
    }

    [Fact]
    public void ToString_ReturnsTheByteCount() => FileSize.Create(4096).ToString().ShouldBe("4096");

    [Fact]
    public void TheOnlyWayToBuildASize_IsTheFactory() =>
        typeof(FileSize).GetConstructors().ShouldBeEmpty();
}
