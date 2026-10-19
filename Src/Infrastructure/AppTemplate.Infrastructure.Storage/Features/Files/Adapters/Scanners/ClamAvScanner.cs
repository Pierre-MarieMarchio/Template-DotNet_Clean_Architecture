using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Infrastructure.Storage.Common.Budgets;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Scanners;

/// <summary>
/// One conversation with <c>clamd</c> in its <c>INSTREAM</c> dialect, on a fresh connection per command.
/// Nothing is unpacked here: archive limits (<c>MaxScanSize</c>, <c>MaxFiles</c>, <c>MaxRecursion</c>)
/// are the daemon's, and a deployment must set them.
/// </summary>
internal static class ClamAvScanner
{
    /// <summary>The <c>z</c> prefix selects the NUL-terminated dialect, whose framing ignores newlines.</summary>
    private static readonly byte[] _command = Encoding.ASCII.GetBytes("zINSTREAM\0");

    /// <summary>A chunk length of zero: "that was all of it".</summary>
    private static readonly byte[] _terminator = [0, 0, 0, 0];

    /// <summary>Large enough that the header is noise, small enough that cancellation is prompt.</summary>
    private const int _chunkSize = 64 * 1024;

    /// <summary>A verdict is a few dozen characters; reading more would hold memory on a daemon's say-so.</summary>
    private const int _maxReplyBytes = 512;

    /// <summary>
    /// Streams <paramref name="content"/> past the daemon and reads its verdict. Past
    /// <paramref name="maxBytes"/> the transfer stops and the outcome is
    /// <see cref="FileScanStatus.NotInspectable"/>. An unreachable or misbehaving daemon is
    /// <see cref="FileScanStatus.Unavailable"/>, never an exception; cancellation propagates.
    /// </summary>
    internal static async Task<FileScanOutcome> ScanAsync(
        string host,
        int port,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();

        try
        {
            await ConnectAsync(client, host, port, cancellationToken);

            await using var socket = client.GetStream();

            await socket.WriteAsync(_command, cancellationToken);

            return await ExchangeAsync(socket, content, maxBytes, cancellationToken);
        }
        catch (SocketException)
        {
            return FileScanOutcome.Unavailable;
        }
        catch (IOException)
        {
            return FileScanOutcome.Unavailable;
        }
    }

    /// <summary>Connects under its own attempt timeout so a silent host cannot spend the whole budget.</summary>
    private static async Task ConnectAsync(TcpClient client, string host, int port, CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(ScannerBudget.AttemptTimeout);

        await client.ConnectAsync(host, port, attempt.Token);
    }

    /// <summary>
    /// Sends the content and reads the verdict concurrently: <c>clamd</c> answers on detection and then
    /// resets a connection with unread bytes, which would lose a verdict read only after sending.
    /// </summary>
    private static async Task<FileScanOutcome> ExchangeAsync(
        Stream socket,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var reply = ReadReplyAsync(socket, exchange.Token);

        bool withinLimit;

        try
        {
            withinLimit = await TrySendAsync(socket, content, maxBytes, cancellationToken);
        }
        catch
        {
            await AbandonAsync(exchange, reply);
            throw;
        }

        if (!withinLimit)
        {
            await AbandonAsync(exchange, reply);

            return FileScanOutcome.NotInspectable;
        }

        return Interpret(await reply);
    }

    /// <summary>Stops waiting for a verdict and observes the read so it is never left unobserved.</summary>
    private static async Task AbandonAsync(CancellationTokenSource exchange, Task<string> reply)
    {
        await exchange.CancelAsync();

        try
        {
            await reply;
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    /// <returns><c>false</c> when the content ran past <paramref name="maxBytes"/>.</returns>
    private static async Task<bool> TrySendAsync(
        Stream socket,
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_chunkSize);

        try
        {
            long sent = 0;
            int read;

            while ((read = await content.ReadAsync(buffer.AsMemory(0, _chunkSize), cancellationToken)) > 0)
            {
                sent += read;

                if (sent > maxBytes)
                {
                    return false;
                }

                await WriteChunkAsync(socket, buffer.AsMemory(0, read), cancellationToken);
            }

            await socket.WriteAsync(_terminator, cancellationToken);
            await socket.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            // The daemon may have answered and hung up; the reply read decides.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return true;
    }

    /// <summary>One <c>INSTREAM</c> chunk: a big-endian length, then the bytes.</summary>
    private static async Task WriteChunkAsync(Stream socket, ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)chunk.Length);

        await socket.WriteAsync(length, cancellationToken);
        await socket.WriteAsync(chunk, cancellationToken);
    }

    /// <summary>Reads up to the terminating NUL, or to <see cref="_maxReplyBytes"/>.</summary>
    private static async Task<string> ReadReplyAsync(Stream socket, CancellationToken cancellationToken)
    {
        byte[] reply = new byte[_maxReplyBytes];
        int filled = 0;

        while (filled < reply.Length)
        {
            int read = await socket.ReadAsync(reply.AsMemory(filled), cancellationToken);

            if (read == 0)
            {
                break;
            }

            filled += read;

            if (Array.IndexOf(reply, (byte)0, 0, filled) >= 0)
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(reply, 0, filled).TrimEnd('\0', '\n', ' ');
    }

    /// <summary>Anything but <c>OK</c>, <c>FOUND</c> or <c>ERROR</c> is no verdict: the only guess would be "clean".</summary>
    private static FileScanOutcome Interpret(string reply)
    {
        if (reply.EndsWith("FOUND", StringComparison.Ordinal))
        {
            return FileScanOutcome.Infected(SignatureOf(reply));
        }

        if (reply.EndsWith("ERROR", StringComparison.Ordinal))
        {
            // Past the daemon's own StreamMaxLength is an answer no retry changes; any other error is a fault.
            return reply.Contains("size limit", StringComparison.OrdinalIgnoreCase)
                ? FileScanOutcome.NotInspectable
                : FileScanOutcome.Unavailable;
        }

        return reply.EndsWith("OK", StringComparison.Ordinal)
            ? FileScanOutcome.Clean
            : FileScanOutcome.Unavailable;
    }

    /// <summary>The name between <c>stream:</c> and <c>FOUND</c>, for a log line only.</summary>
    private static string SignatureOf(string reply)
    {
        const string prefix = "stream:";

        int start = reply.IndexOf(prefix, StringComparison.Ordinal);
        int end = reply.LastIndexOf("FOUND", StringComparison.Ordinal);

        if (start < 0 || end <= start + prefix.Length)
        {
            return reply;
        }

        string signature = reply[(start + prefix.Length)..end].Trim();

        return signature.Length == 0 ? reply : signature;
    }
}
