using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Scanners;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Adapters.Scanners;

/// <summary>
/// The <c>INSTREAM</c> conversation, against a daemon written here that speaks the protocol back.
/// <para>
/// <b>A fake that reassembles the framing rather than one that ignores it</b>, because the framing is
/// the only thing in this file that could silently be wrong: a length written little-endian, or a
/// chunk sent without its header, produces a client that hangs or a daemon that scans the wrong
/// bytes — and against a stub that just answered "OK" every one of those would pass.
/// </para>
/// <para>
/// It listens on loopback, on a port the operating system picks, so the suite needs nothing
/// installed and two runs at once cannot collide.
/// </para>
/// </summary>
public sealed class ClamAvScannerTests
{
    private static readonly byte[] _head = Encoding.ASCII.GetBytes("HEAD-");

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// The property the whole adapter rests on: the daemon receives every byte of the content, in
    /// order, exactly once, across as many chunks as it takes.
    /// </summary>
    [Fact]
    public async Task TheWholeObject_ReachesTheDaemonInOrder()
    {
        byte[] rest = Encoding.ASCII.GetBytes(new string('x', 200_000));
        await using var daemon = await FakeDaemon.StartAsync("stream: OK", TestToken);

        var outcome = await ScanAsync(daemon, rest);

        outcome.Status.ShouldBe(FileScanStatus.Clean);
        (await daemon.Received).ShouldBe([.. _head, .. rest]);
    }

    [Fact]
    public async Task AnObjectSmallerThanOneChunk_StillArrivesWhole()
    {
        await using var daemon = await FakeDaemon.StartAsync("stream: OK", TestToken);

        await ScanAsync(daemon, Encoding.ASCII.GetBytes("tail"));

        (await daemon.Received).ShouldBe(Encoding.ASCII.GetBytes("HEAD-tail"));
    }

    [Fact]
    public async Task ACleanVerdict_IsReportedAsClean()
    {
        await using var daemon = await FakeDaemon.StartAsync("stream: OK", TestToken);

        var outcome = await ScanAsync(daemon, []);

        outcome.Status.ShouldBe(FileScanStatus.Clean);
        outcome.MalwareSignature.ShouldBeNull();
    }

    [Fact]
    public async Task ADetection_IsReportedWithTheNameTheDaemonGaveIt()
    {
        await using var daemon = await FakeDaemon.StartAsync("stream: Win.Test.EICAR_HDB-1 FOUND", TestToken);

        var outcome = await ScanAsync(daemon, []);

        outcome.Status.ShouldBe(FileScanStatus.Infected);
        outcome.MalwareSignature.ShouldBe("Win.Test.EICAR_HDB-1");
    }

    /// <summary>
    /// The one error that is an answer rather than a fault. Read as a fault it would be retried for
    /// ever against an object whose size will never change; read as a pass it would make "upload
    /// something bigger than the daemon accepts" the way to skip the scan.
    /// </summary>
    [Fact]
    public async Task TheStreamSizeLimit_IsReportedAsContentNothingCanExamine()
    {
        await using var daemon = await FakeDaemon.StartAsync("INSTREAM size limit exceeded. ERROR", TestToken);

        var outcome = await ScanAsync(daemon, []);

        outcome.Status.ShouldBe(FileScanStatus.NotInspectable);
    }

    /// <summary>
    /// Every other error is a fault — a signature database that failed to load, a daemon out of file
    /// descriptors — and a fault is retried rather than turned into a refusal of somebody's file.
    /// </summary>
    [Fact]
    public async Task AnyOtherError_IsReportedAsNoVerdict()
    {
        await using var daemon = await FakeDaemon.StartAsync("Can't allocate memory ERROR", TestToken);

        var outcome = await ScanAsync(daemon, []);

        outcome.Status.ShouldBe(FileScanStatus.Unavailable);
    }

    /// <summary>
    /// An answer this code does not understand must not be guessed at, because the only direction to
    /// guess in is "clean".
    /// </summary>
    [Fact]
    public async Task AnAnswerInNoKnownShape_IsReportedAsNoVerdict()
    {
        await using var daemon = await FakeDaemon.StartAsync("what", TestToken);

        var outcome = await ScanAsync(daemon, []);

        outcome.Status.ShouldBe(FileScanStatus.Unavailable);
    }

    /// <summary>
    /// Nothing is listening, which is what a scanner that is down or misconfigured looks like. It is
    /// reported as no verdict — never as clean — so the pass above leaves the file where it is.
    /// </summary>
    [Fact]
    public async Task ADaemonThatIsNotThere_IsReportedAsNoVerdict()
    {
        int deadPort = FakeDaemon.APortNothingIsListeningOn();

        var outcome = await ClamAvScanner.ScanAsync(
            IPAddress.Loopback.ToString(),
            deadPort,
            new MemoryStream(_head),
            _noLimit,
            TestToken);

        outcome.Status.ShouldBe(FileScanStatus.Unavailable);
        outcome.MalwareSignature.ShouldBeNull();
    }

    /// <summary>
    /// clamd stops reading and answers as soon as it has found something, so a large infected file
    /// routinely breaks the pipe mid-transfer with the verdict already waiting on the socket. Giving
    /// up there would report the one case the scanner exists for as an outage.
    /// </summary>
    [Fact]
    public async Task ADaemonThatAnswersBeforeTheTransferEnds_IsStillHeard()
    {
        await using var daemon = await FakeDaemon.StartAsync(
            "stream: Win.Test.EICAR_HDB-1 FOUND",
            TestToken,
            answerImmediately: true);

        var outcome = await ScanAsync(daemon, Encoding.ASCII.GetBytes(new string('x', 5_000_000)));

        outcome.Status.ShouldBe(FileScanStatus.Infected);
    }

    /// <summary>
    /// Content past the ceiling is refused as not inspectable while it is being sent — the length of
    /// a store's response stream is not always known up front — rather than as a pipe the daemon
    /// breaks midway, which would read as an outage and be retried for ever.
    /// </summary>
    [Fact]
    public async Task ContentPastTheCeiling_IsReportedAsNotInspectable()
    {
        await using var daemon = await FakeDaemon.StartAsync("stream: OK", TestToken);

        var outcome = await ClamAvScanner.ScanAsync(
            IPAddress.Loopback.ToString(),
            daemon.Port,
            new MemoryStream(new byte[200_000]),
            100_000,
            TestToken);

        outcome.ShouldBe(FileScanOutcome.NotInspectable);
    }

    [Fact]
    public async Task ContentExactlyAtTheCeiling_IsScanned()
    {
        await using var daemon = await FakeDaemon.StartAsync("stream: OK", TestToken);

        var outcome = await ClamAvScanner.ScanAsync(
            IPAddress.Loopback.ToString(),
            daemon.Port,
            new MemoryStream(new byte[100_000]),
            100_000,
            TestToken);

        outcome.ShouldBe(FileScanOutcome.Clean);
    }

    private const long _noLimit = long.MaxValue;

    private static Task<FileScanOutcome> ScanAsync(FakeDaemon daemon, byte[] rest) =>
        ClamAvScanner.ScanAsync(
            IPAddress.Loopback.ToString(),
            daemon.Port,
            new MemoryStream([.. _head, .. rest]),
            _noLimit,
            TestToken);

    /// <summary>
    /// A daemon that speaks <c>INSTREAM</c>: it reads the command, reassembles the framed chunks, and
    /// answers with the one line it was told to.
    /// </summary>
    private sealed class FakeDaemon : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource<byte[]> _received = new();

        private FakeDaemon(TcpListener listener) => _listener = listener;

        internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>What the daemon reassembled out of the frames it was sent.</summary>
        internal Task<byte[]> Received => _received.Task;

        /// <param name="answerImmediately">Answers and closes as soon as the command arrives, without
        /// draining the object — which is what a real daemon does the moment it has a detection.</param>
        internal static async Task<FakeDaemon> StartAsync(
            string reply,
            CancellationToken cancellationToken,
            bool answerImmediately = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var daemon = new FakeDaemon(listener);
            _ = daemon.ServeAsync(reply, answerImmediately, cancellationToken);

            await Task.Yield();

            return daemon;
        }

        internal static int APortNothingIsListeningOn()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            return port;
        }

        public ValueTask DisposeAsync()
        {
            _listener.Dispose();
            _received.TrySetResult([]);

            return ValueTask.CompletedTask;
        }

        private async Task ServeAsync(string reply, bool answerImmediately, CancellationToken cancellationToken)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();

                // "zINSTREAM\0" — ten bytes, and the length is part of what is being asserted: a
                // client that sent the newline dialect instead would desynchronise here.
                byte[] command = new byte[10];
                await stream.ReadExactlyAsync(command, cancellationToken);
                Encoding.ASCII.GetString(command).ShouldBe("zINSTREAM\0");

                if (!answerImmediately)
                {
                    _received.TrySetResult(await ReadFramedAsync(stream, cancellationToken));
                }

                await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                _received.TrySetException(exception);
            }
        }

        private static async Task<byte[]> ReadFramedAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var payload = new MemoryStream();
            byte[] length = new byte[4];

            while (true)
            {
                await stream.ReadExactlyAsync(length, cancellationToken);

                uint size = BinaryPrimitives.ReadUInt32BigEndian(length);

                if (size == 0)
                {
                    return payload.ToArray();
                }

                byte[] chunk = new byte[size];
                await stream.ReadExactlyAsync(chunk, cancellationToken);
                await payload.WriteAsync(chunk, cancellationToken);
            }
        }
    }
}
