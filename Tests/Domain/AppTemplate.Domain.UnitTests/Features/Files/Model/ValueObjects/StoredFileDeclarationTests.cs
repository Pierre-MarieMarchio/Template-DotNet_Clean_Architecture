using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Model.ValueObjects;

public sealed class StoredFileDeclarationTests
{
    private static readonly StoredFileName _name = StoredFileName.Create("quarterly-report.pdf");
    private static readonly DeclaredMediaType _mediaType = DeclaredMediaType.Create("application/pdf");
    private static readonly FileSize _size = FileSize.Create(4096);

    private static readonly Sha256Checksum _checksum =
        Sha256Checksum.Create("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

    [Fact]
    public void Create_KeepsEverythingItIsGiven()
    {
        var declaration = StoredFileDeclaration.Create(_name, _mediaType, _size, _checksum);

        declaration.Name.ShouldBe(_name);
        declaration.MediaType.ShouldBe(_mediaType);
        declaration.Size.ShouldBe(_size);
        declaration.Checksum.ShouldBe(_checksum);
    }

    [Fact]
    public void TwoDeclarationsOfTheSameFile_AreEqual() =>
        StoredFileDeclaration.Create(_name, _mediaType, _size, _checksum)
            .ShouldBe(StoredFileDeclaration.Create(_name, _mediaType, _size, _checksum));

    [Fact]
    public void Create_Rejects_AMissingName() =>
        Should.Throw<ArgumentNullException>(() => StoredFileDeclaration.Create(null!, _mediaType, _size, _checksum));

    [Fact]
    public void Create_Rejects_AMissingMediaType() =>
        Should.Throw<ArgumentNullException>(() => StoredFileDeclaration.Create(_name, null!, _size, _checksum));

    [Fact]
    public void Create_Rejects_AMissingSize() =>
        Should.Throw<ArgumentNullException>(() => StoredFileDeclaration.Create(_name, _mediaType, null!, _checksum));

    [Fact]
    public void Create_Rejects_AMissingChecksum() =>
        Should.Throw<ArgumentNullException>(() => StoredFileDeclaration.Create(_name, _mediaType, _size, null!));
}
