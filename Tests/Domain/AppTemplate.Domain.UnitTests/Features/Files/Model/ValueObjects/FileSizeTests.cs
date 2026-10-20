using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Model.ValueObjects;

public sealed class FileSizeTests
{
    [Fact]
    public void Create_KeepsTheByteCount() => FileSize.Create(4096).Bytes.ShouldBe(4096);

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

    [Fact]
    public void Create_Rejects_OneByteBeyondTheMaximum() =>
        Should.Throw<DomainException>(() => FileSize.Create(FileSize.MaxBytes + 1));

    [Fact]
    public void Create_Rejects_TheLargestPossibleLong() =>
        Should.Throw<DomainException>(() => FileSize.Create(long.MaxValue));

    [Fact]
    public void TheMaximum_IsTheSinglePartCeiling() => FileSize.MaxBytes.ShouldBe(5L * 1024 * 1024 * 1024);

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
