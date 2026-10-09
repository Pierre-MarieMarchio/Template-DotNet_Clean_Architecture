using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

/// <summary>
/// The scanner double, reached through its port out of a composed scope: it answers what
/// <c>ArrangedScans</c> holds for a key, and clean when nothing is arranged.
/// </summary>
public sealed class InMemoryFileContentScannerTests
{
    private const string _key = "t0/202608/0123456789abcdef0123456789abcdef";

    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>The default a deployment without a scanner stands for.</summary>
    [Fact]
    public async Task NothingArranged_IsClean()
    {
        await using var host = FileContentHost.Compose();

        (await ScanAsync(host)).ShouldBe(FileScanOutcome.Clean);
    }

    [Fact]
    public async Task AnArrangedDetection_IsReportedWithItsSignature()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.ScansOf(host).Infect(_key, "Win.Test.EICAR_HDB-1");

        var outcome = await ScanAsync(host);

        outcome.Status.ShouldBe(FileScanStatus.Infected);
        outcome.MalwareSignature.ShouldBe("Win.Test.EICAR_HDB-1");
    }

    [Fact]
    public async Task AnObjectTheScannerRefuses_IsReportedAsNotInspectable()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.ScansOf(host).RefuseAsTooLarge(_key);

        (await ScanAsync(host)).Status.ShouldBe(FileScanStatus.NotInspectable);
    }

    [Fact]
    public async Task AnArrangedOutage_IsReportedWithNoVerdict()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.ScansOf(host).MakeUnavailable(_key);

        (await ScanAsync(host)).Status.ShouldBe(FileScanStatus.Unavailable);
    }

    /// <summary>
    /// Arrangements are per key, so a test that infects one object cannot refuse every other.
    /// </summary>
    [Fact]
    public async Task AnArrangementReachesOnlyTheKeyItNames()
    {
        const string otherKey = "t0/202608/fedcba9876543210fedcba9876543210";
        await using var host = FileContentHost.Compose();
        FileContentHost.ScansOf(host).Infect(_key, "Win.Test.EICAR_HDB-1");

        using var scope = host.CreateScope();
        using var content = new MemoryStream(_png);

        (await FileContentHost.ScannerIn(scope).ScanAsync(otherKey, content, TestToken))
            .ShouldBe(FileScanOutcome.Clean);
    }

    [Fact]
    public async Task Clear_ForgetsEveryArrangement()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.ScansOf(host).Infect(_key, "Win.Test.EICAR_HDB-1");

        FileContentHost.ScansOf(host).Clear();

        (await ScanAsync(host)).ShouldBe(FileScanOutcome.Clean);
    }

    /// <summary>Like a real scanner, the double reads what it is handed to the end.</summary>
    [Fact]
    public async Task TheContent_IsReadToTheEnd()
    {
        await using var host = FileContentHost.Compose();
        using var scope = host.CreateScope();
        using var content = new MemoryStream(_png);

        await FileContentHost.ScannerIn(scope).ScanAsync(_key, content, TestToken);

        content.Position.ShouldBe(content.Length);
    }

    [Fact]
    public async Task AnEmptyKey_IsARejectedArgument()
    {
        await using var host = FileContentHost.Compose();
        using var scope = host.CreateScope();

        await Should.ThrowAsync<ArgumentException>(
            () => FileContentHost.ScannerIn(scope).ScanAsync(" ", Stream.Null, TestToken));
    }

    private static async Task<FileScanOutcome> ScanAsync(IServiceProvider host)
    {
        using var scope = host.CreateScope();
        using var content = new MemoryStream(_png);

        return await FileContentHost.ScannerIn(scope).ScanAsync(_key, content, TestToken);
    }
}
