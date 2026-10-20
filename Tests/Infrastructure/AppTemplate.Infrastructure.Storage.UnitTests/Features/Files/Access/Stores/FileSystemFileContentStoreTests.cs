using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Access.Stores;

public sealed class FileSystemFileContentStoreTests : IDisposable
{
    private const string _objectKey = "t0/202608/9f2c1d7a4b6e8f0132547698badcfe10";

    private const string _mediaType = "application/pdf";

    private static readonly byte[] _bytes = Encoding.UTF8.GetBytes("%PDF-1.7 the deposited bytes");

    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_bytes));

    private readonly TemporaryRoot _root = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task AWrittenObject_IsReadDescribedAndDeleted()
    {
        var store = Store();

        (await WriteAsync(store, _bytes, _bytes.Length, _checksum)).ShouldBe(ContentWriteOutcome.Written);

        await using (var read = await store.OpenReadAsync(_objectKey, TestToken))
        {
            read.ShouldNotBeNull();
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy, TestToken);
            copy.ToArray().ShouldBe(_bytes);
        }

        (await store.DescribeAsync(_objectKey, TestToken))
            .ShouldBe(new StoredObjectDescription(_bytes.Length, _checksum));

        await store.DeleteAsync(_objectKey, TestToken);

        (await store.OpenReadAsync(_objectKey, TestToken)).ShouldBeNull();
        (await store.DescribeAsync(_objectKey, TestToken)).ShouldBeNull();
    }

    [Fact]
    public async Task AWrittenObject_IsTheFileItsKeyNamesAndNothingElse()
    {
        await WriteAsync(Store(), _bytes, _bytes.Length, _checksum);

        _root.AllFiles().ShouldBe([_objectKey]);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public async Task ABodyThatDisagreesWithItsDeclaration_LeavesNothing(int sizeOffset, bool wrongDigest)
    {
        var outcome = await WriteAsync(
            Store(),
            _bytes,
            _bytes.Length + sizeOffset,
            wrongDigest ? Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])) : _checksum);

        outcome.ShouldBe(ContentWriteOutcome.ContentMismatch);
        _root.AllFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheDeclaredDigest_IsComparedWhateverItsCase() =>
        (await WriteAsync(Store(), _bytes, _bytes.Length, _checksum.ToUpperInvariant()))
            .ShouldBe(ContentWriteOutcome.Written);

    [Fact]
    public async Task ABodyThatFailsMidway_LeavesNothing()
    {
        await Should.ThrowAsync<IOException>(Store().WriteAsync(
            _objectKey,
            new FailingStream(),
            _mediaType,
            _bytes.Length,
            _checksum,
            TestToken));

        _root.AllFiles().ShouldBeEmpty();
    }

    [Fact]
    public async Task WritingAKeyTwice_KeepsOneObject()
    {
        var store = Store();

        await WriteAsync(store, _bytes, _bytes.Length, _checksum);
        (await WriteAsync(store, _bytes, _bytes.Length, _checksum)).ShouldBe(ContentWriteOutcome.Written);

        _root.AllFiles().ShouldBe([_objectKey]);
    }

    [Fact]
    public async Task DeletingAKeyThatHoldsNothing_Succeeds()
    {
        var store = Store();

        await Should.NotThrowAsync(store.DeleteAsync(_objectKey, TestToken));
        await WriteAsync(store, _bytes, _bytes.Length, _checksum);
        await store.DeleteAsync(_objectKey, TestToken);
        await Should.NotThrowAsync(store.DeleteAsync(_objectKey, TestToken));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("t0/../../outside")]
    [InlineData("/etc/passwd")]
    [InlineData(".")]
    public async Task AKeyThatEscapesTheRoot_IsRefused(string objectKey)
    {
        var store = Store();

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

    private FileSystemFileContentStore Store() => new(_root.Options);

    private static Task<ContentWriteOutcome> WriteAsync(
        FileSystemFileContentStore store,
        byte[] bytes,
        long declaredSize,
        string declaredChecksum) =>
        store.WriteAsync(_objectKey, new MemoryStream(bytes), _mediaType, declaredSize, declaredChecksum, TestToken);

    private sealed class FailingStream : Stream
    {
        private bool _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served)
            {
                throw new IOException("The connection was reset.");
            }

            _served = true;
            buffer[offset] = 1;

            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
