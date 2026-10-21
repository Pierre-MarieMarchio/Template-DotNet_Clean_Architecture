using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;
using AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Contracts;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Access.Stores;

public sealed class FileSystemFileContentStoreTests : FileContentStoreContract, IDisposable
{
    private const string _objectKey = "t0/202608/9f2c1d7a4b6e8f0132547698badcfe10";

    private const string _mediaType = "application/pdf";

    private static readonly byte[] _bytes = Encoding.UTF8.GetBytes("%PDF-1.7 the deposited bytes");

    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_bytes));

    private readonly TemporaryRoot _root = new();

    protected override IFileContentStore Store => new FileSystemFileContentStore(_root.Options);

    public void Dispose() => _root.Dispose();

    protected override void ShouldLeaveNoTrace() => _root.AllFiles().ShouldBeEmpty();

    [Fact]
    public async Task AWrittenObject_IsTheFileItsKeyNamesAndNothingElse()
    {
        await WriteAsync(FileSystemStore(), _bytes, _bytes.Length, _checksum);

        _root.AllFiles().ShouldBe([_objectKey]);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("t0/../../outside")]
    [InlineData("/etc/passwd")]
    [InlineData(".")]
    public async Task AKeyThatEscapesTheRoot_IsRefused(string objectKey)
    {
        var store = FileSystemStore();

        await Should.ThrowAsync<ArgumentException>(() => store.WriteAsync(
            objectKey,
            new MemoryStream(_bytes),
            _mediaType,
            _bytes.Length,
            _checksum,
            TestToken));
        await Should.ThrowAsync<ArgumentException>(() => store.OpenReadAsync(objectKey, TestToken));
        await Should.ThrowAsync<ArgumentException>(() => store.DescribeAsync(objectKey, TestToken));
        await Should.ThrowAsync<ArgumentException>(() => store.DeleteAsync(objectKey, TestToken));
    }

    [Fact]
    public void ASiblingSharingTheRootsPrefix_IsOutsideIt() =>
        Should.Throw<ArgumentException>(() => FileSystemFileContentStore.PathFor(
            _root.Path,
            $"../{Path.GetFileName(_root.Path)}-sibling/file"));

    private FileSystemFileContentStore FileSystemStore() => new(_root.Options);

    private static Task<ContentWriteOutcome> WriteAsync(
        FileSystemFileContentStore store,
        byte[] bytes,
        long declaredSize,
        string declaredChecksum) =>
        store.WriteAsync(_objectKey, new MemoryStream(bytes), _mediaType, declaredSize, declaredChecksum, TestToken);
}
