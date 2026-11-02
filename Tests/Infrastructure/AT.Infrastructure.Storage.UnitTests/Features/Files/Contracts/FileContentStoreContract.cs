using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Contracts;

public abstract class FileContentStoreContract
{
    private const string _mediaType = "application/pdf";

    private static readonly byte[] _bytes = "%PDF-1.7 the deposited bytes"u8.ToArray();

    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_bytes));

    protected static CancellationToken TestToken => TestContext.Current.CancellationToken;

    protected abstract IFileContentStore Store { get; }

    protected virtual string NewKey() => $"t0/202608/{Guid.CreateVersion7():N}";

    protected virtual void ShouldLeaveNoTrace()
    {
    }

    [Fact]
    public async Task AWrittenObject_IsReadDescribedAndDeleted()
    {
        string key = NewKey();

        (await WriteAsync(key, new MemoryStream(_bytes), _bytes.Length, _checksum)).ShouldBe(ContentWriteOutcome.Written);

        (await ReadAsync(key)).ShouldBe(_bytes);
        (await Store.DescribeAsync(key, TestToken)).ShouldBe(new StoredObjectDescription(_bytes.Length, _checksum));

        await Store.DeleteAsync(key, TestToken);

        await ShouldHoldNothingAsync(key);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public async Task ABodyThatDisagreesWithItsDeclaration_IsRefusedAndLeavesNothing(int sizeOffset, bool wrongDigest)
    {
        string key = NewKey();

        var outcome = await WriteAsync(
            key,
            new MemoryStream(_bytes),
            _bytes.Length + sizeOffset,
            wrongDigest ? Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])) : _checksum);

        outcome.ShouldBe(ContentWriteOutcome.ContentMismatch);
        await ShouldHoldNothingAsync(key);
    }

    [Fact]
    public async Task TheDeclaredDigest_IsComparedWhateverItsCase() =>
        (await WriteAsync(NewKey(), new MemoryStream(_bytes), _bytes.Length, _checksum.ToUpperInvariant()))
            .ShouldBe(ContentWriteOutcome.Written);

    [Fact]
    public async Task ABodyThatFailsMidway_LeavesNothing()
    {
        string key = NewKey();

        await Should.ThrowAsync<IOException>(WriteAsync(key, new FailingStream(), _bytes.Length, _checksum));

        await ShouldHoldNothingAsync(key);
    }

    [Fact]
    public async Task WritingTheSameBytesTwice_KeepsThem()
    {
        string key = NewKey();

        await WriteAsync(key, new MemoryStream(_bytes), _bytes.Length, _checksum);
        (await WriteAsync(key, new MemoryStream(_bytes), _bytes.Length, _checksum)).ShouldBe(ContentWriteOutcome.Written);

        (await ReadAsync(key)).ShouldBe(_bytes);
    }

    [Fact]
    public async Task AKeyThatHoldsNothing_IsNeitherReadNorDescribed_AndDeletesQuietly()
    {
        string key = NewKey();

        await ShouldHoldNothingAsync(key);
        await Should.NotThrowAsync(Store.DeleteAsync(key, TestToken));
    }

    private Task<ContentWriteOutcome> WriteAsync(string key, Stream content, long declaredSize, string declaredChecksum) =>
        Store.WriteAsync(key, content, _mediaType, declaredSize, declaredChecksum, TestToken);

    private async Task<byte[]> ReadAsync(string key)
    {
        await using var read = await Store.OpenReadAsync(key, TestToken);

        read.ShouldNotBeNull();

        using var copy = new MemoryStream();
        await read.CopyToAsync(copy, TestToken);

        return copy.ToArray();
    }

    private async Task ShouldHoldNothingAsync(string key)
    {
        (await Store.OpenReadAsync(key, TestToken)).ShouldBeNull();
        (await Store.DescribeAsync(key, TestToken)).ShouldBeNull();
        ShouldLeaveNoTrace();
    }

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
