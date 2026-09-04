using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Infrastructure.Storage.Common.Budgets;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Scanners;

/// <summary>
/// One conversation with <c>clamd</c>, in its <c>INSTREAM</c> dialect.
/// <para>
/// <b>It is a protocol and not a service</b>, which is why it is static and takes everything it needs
/// as arguments: there is no state to hold between calls, no client to pool — <c>clamd</c> wants a
/// fresh connection per command — and nothing to substitute, since the thing a test would want to
/// replace is the whole port above it.
/// </para>
/// <para>
/// <b>Nothing is unpacked here.</b> The stream is copied past the daemon in fixed chunks and this
/// process never interprets a byte of it, so the decompression bomb <c>SECURITY.md</c> says is not
/// addressed is not opened here either. <c>clamd</c> does unpack archives, in its own address space,
/// under its own <c>MaxScanSize</c>, <c>MaxFiles</c> and <c>MaxRecursion</c> — <b>which a deployment
/// owes itself to set</b>. Those limits are the bound on that hazard, and they are not settable from
/// here.
/// </para>
/// </summary>
internal static class ClamAvScanner
{
    /// <summary>
    /// The <c>z</c> prefix asks <c>clamd</c> for the NUL-terminated dialect, which is the one whose
    /// framing does not depend on newlines appearing nowhere in the payload.
    /// </summary>
    private static readonly byte[] _command = Encoding.ASCII.GetBytes("zINSTREAM\0");

    /// <summary>A chunk length of zero, which is how <c>INSTREAM</c> says "that was all of it".</summary>
    private static readonly byte[] _terminator = [0, 0, 0, 0];

    /// <summary>
    /// 64 KiB per chunk. Large enough that the four-byte header is noise, small enough that a
    /// cancelled scan stops promptly rather than at the end of the current write.
    /// </summary>
    private const int _chunkSize = 64 * 1024;

    /// <summary>
    /// The most of a reply that will be read. A verdict is a few dozen characters; anything past
    /// this is a daemon that is not answering the protocol, and reading it unbounded would be a way
    /// to make this process hold memory on somebody else's say-so.
    /// </summary>
    private const int _maxReplyBytes = 512;

    /// <summary>
    /// Streams <paramref name="head"/> followed by <paramref name="rest"/> past the daemon and reads
    /// its verdict.
    /// </summary>
    /// <param name="head">The prefix the caller has already read out of the object. It has to be
    /// sent too — the scanner needs the whole file, and the bytes are not in the stream any
    /// more.</param>
    /// <param name="rest">Everything after the prefix, read once, forwards, and never buffered.</param>
    /// <returns>A status and, when something was found, its name. It never throws to report a
    /// verdict: an unreachable or misbehaving daemon is
    /// <see cref="ContentInspectionStatus.Unavailable"/>, which the policy above reads as "ask
    /// again" rather than as either answer.</returns>
    internal static async Task<(ContentInspectionStatus Status, string? Signature)> ScanAsync(
        string host,
        int port,
        ReadOnlyMemory<byte> head,
        Stream rest,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();

        try
        {
            await ConnectAsync(client, host, port, cancellationToken);

            await using var socket = client.GetStream();

            await socket.WriteAsync(_command, cancellationToken);

            // A write failing part-way through is not a failed scan. clamd stops reading and answers
            // as soon as it has found something, so a large infected file regularly produces a
            // broken pipe here — and the verdict is already waiting on the socket. Giving up at this
            // point would report the one case the scanner is for as an outage.
            await TrySendAsync(socket, head, rest, cancellationToken);

            return Interpret(await ReadReplyAsync(socket, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller's own cancellation — a host shutting down. Let it through, rather than
            // reporting a shutdown as a scanner that could not be reached.
            throw;
        }
        catch (OperationCanceledException)
        {
            // The budget expired. The file has no verdict, and the next pass will try again.
            return (ContentInspectionStatus.Unavailable, null);
        }
        catch (SocketException)
        {
            return (ContentInspectionStatus.Unavailable, null);
        }
        catch (IOException)
        {
            return (ContentInspectionStatus.Unavailable, null);
        }
    }

    /// <summary>
    /// Connects under its own attempt timeout, inside the caller's total budget. Connecting is the
    /// operation that hangs when the host is wrong rather than down, and a name that resolves to
    /// something silent would otherwise spend the whole budget here and leave none for the transfer.
    /// </summary>
    private static async Task ConnectAsync(
        TcpClient client,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(ScannerBudget.AttemptTimeout);

        await client.ConnectAsync(host, port, attempt.Token);
    }

    private static async Task TrySendAsync(
        Stream socket,
        ReadOnlyMemory<byte> head,
        Stream rest,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_chunkSize);

        try
        {
            await WriteChunkAsync(socket, head, cancellationToken);

            int read;

            while ((read = await rest.ReadAsync(buffer.AsMemory(0, _chunkSize), cancellationToken)) > 0)
            {
                await WriteChunkAsync(socket, buffer.AsMemory(0, read), cancellationToken);
            }

            await socket.WriteAsync(_terminator, cancellationToken);
            await socket.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            // Deliberately swallowed: see the caller. The reply is read either way, and a socket
            // that really is gone fails there instead, where it is reported as no verdict.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// One <c>INSTREAM</c> chunk: a big-endian length, then the bytes. A zero-length chunk would
    /// terminate the stream, so an empty read is skipped rather than sent.
    /// </summary>
    private static async Task WriteChunkAsync(
        Stream socket,
        ReadOnlyMemory<byte> chunk,
        CancellationToken cancellationToken)
    {
        if (chunk.IsEmpty)
        {
            return;
        }

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

    /// <summary>
    /// Turns <c>clamd</c>'s one-line answer into a status.
    /// <para>
    /// The three shapes are <c>stream: OK</c>, <c>stream: &lt;name&gt; FOUND</c> and
    /// <c>&lt;reason&gt; ERROR</c>. Anything else is a daemon this code does not understand, and
    /// guessing at it would mean guessing in the direction of "clean" — so it is reported as no
    /// verdict instead.
    /// </para>
    /// </summary>
    private static (ContentInspectionStatus Status, string? Signature) Interpret(string reply)
    {
        if (reply.EndsWith("FOUND", StringComparison.Ordinal))
        {
            return (ContentInspectionStatus.Infected, SignatureOf(reply));
        }

        if (reply.EndsWith("ERROR", StringComparison.Ordinal))
        {
            // The one error that is an answer rather than a fault: the object is past the daemon's
            // own StreamMaxLength, which no retry changes. Everything else — a database that failed
            // to load, a daemon out of file descriptors — is a fault, and faults are retried.
            return reply.Contains("size limit", StringComparison.OrdinalIgnoreCase)
                ? (ContentInspectionStatus.NotInspectable, null)
                : (ContentInspectionStatus.Unavailable, null);
        }

        return reply.EndsWith("OK", StringComparison.Ordinal)
            ? (ContentInspectionStatus.Clean, null)
            : (ContentInspectionStatus.Unavailable, null);
    }

    /// <summary>
    /// The name between <c>stream:</c> and <c>FOUND</c>. Reported only to a log line — it is a
    /// string from somebody else's signature database and it never reaches a client.
    /// </summary>
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
