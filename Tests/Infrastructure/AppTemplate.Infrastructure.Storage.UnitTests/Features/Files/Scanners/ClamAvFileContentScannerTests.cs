using System.Net;
using System.Net.Sockets;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Scanners;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Scanners;

/// <summary>
/// The adapter around the <c>INSTREAM</c> conversation: the decisions it makes before any socket is
/// opened, and what an unreachable daemon becomes.
/// </summary>
public sealed class ClamAvFileContentScannerTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// A stream whose length is known is judged before a byte is sent. The host here resolves to
    /// nothing listening, so any connection attempt would answer Unavailable instead.
    /// </summary>
    [Fact]
    public async Task ContentKnownToBePastTheCeiling_IsNotInspectableWithoutConnecting()
    {
        var scanner = Scanner(port: APortNothingIsListeningOn(), maxScannableBytes: 1024 * 1024);

        var outcome = await scanner.ScanAsync(
            "t0/202608/0123456789abcdef0123456789abcdef",
            new MemoryStream(new byte[(1024 * 1024) + 1]),
            TestToken);

        outcome.ShouldBe(FileScanOutcome.NotInspectable);
    }

    [Fact]
    public async Task AnUnreachableDaemon_IsNoVerdict()
    {
        var scanner = Scanner(port: APortNothingIsListeningOn(), maxScannableBytes: 1024 * 1024);

        var outcome = await scanner.ScanAsync(
            "t0/202608/0123456789abcdef0123456789abcdef",
            new MemoryStream([1, 2, 3]),
            TestToken);

        outcome.ShouldBe(FileScanOutcome.Unavailable);
    }

    [Fact]
    public async Task TheNoOpScanner_FindsNothingAndReadsNothing()
    {
        using var content = new MemoryStream([1, 2, 3]);

        var outcome = await new NoOpFileContentScanner().ScanAsync(
            "t0/202608/0123456789abcdef0123456789abcdef",
            content,
            TestToken);

        outcome.ShouldBe(FileScanOutcome.Clean);
        content.Position.ShouldBe(0);
    }

    private static ClamAvFileContentScanner Scanner(int port, long maxScannableBytes) =>
        new(
            Microsoft.Extensions.Options.Options.Create(new FilesScanningOptions
            {
                Enabled = true,
                Host = IPAddress.Loopback.ToString(),
                Port = port,
                MaxScannableBytes = maxScannableBytes,
            }),
            NullLogger<ClamAvFileContentScanner>.Instance);

    private static int APortNothingIsListeningOn()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        return port;
    }
}
