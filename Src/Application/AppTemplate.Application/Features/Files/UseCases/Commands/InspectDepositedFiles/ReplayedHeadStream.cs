namespace AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;

/// <summary>
/// A read-only, forward-only stream that yields bytes already read off the front of a stream, then
/// the rest of that stream — so the content is opened once for both the head and the scan.
/// Disposing it leaves the underlying stream open; its owner disposes it.
/// </summary>
internal sealed class ReplayedHeadStream(ReadOnlyMemory<byte> head, Stream rest) : Stream
{
    private ReadOnlyMemory<byte> _head = head;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_head.IsEmpty)
        {
            return rest.Read(buffer);
        }

        int copied = Math.Min(buffer.Length, _head.Length);
        _head.Span[..copied].CopyTo(buffer);
        _head = _head[copied..];

        return copied;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_head.IsEmpty)
        {
            return rest.ReadAsync(buffer, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
