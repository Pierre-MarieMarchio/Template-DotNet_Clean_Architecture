using System.Net;
using System.Net.Sockets;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Scanners;
using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Adapters.Scanners;

public sealed class ClamAvFileContentScannerTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ContentKnownToBePastTheCeiling_IsNotInspectableWithoutConnecting()
    {
        var scanner = Scanner(port: APortNothingIsListeningOn(), maxScannableBytes: 1024 * 1024);

        var outcome = await scanner.ScanAsync(
            "t0/202608/0123456789abcdef0123456789abcdef",
            new MemoryStream([1, 2, 3]),
            sizeInBytes: (1024 * 1024) + 1,
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
            sizeInBytes: 3,
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
            content.Length,
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
