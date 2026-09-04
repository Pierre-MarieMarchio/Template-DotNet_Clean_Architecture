using System.Text;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

/// <summary>
/// The inspection double, reached through its port out of a composed scope.
/// <para>
/// The tests below run the real <c>StoredFileContentPolicy</c> over what the double answers, and
/// that is the point of the double keeping the bytes a deposit left: a test deposits an actual SVG
/// and an actual refusal comes back, so the signature table going wrong turns these red rather than
/// leaving them green against a verdict the test itself supplied.
/// </para>
/// </summary>
public sealed class InMemoryFileContentInspectorTests
{
    private const string _key = "t0/202608/0123456789abcdef0123456789abcdef";

    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    private static readonly byte[] _svg =
        Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script/></svg>");

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADepositedObject_IsReportedCleanWithItsOwnLeadingBytes()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", _png);

        var outcome = await InspectAsync(host);

        outcome.Status.ShouldBe(ContentInspectionStatus.Clean);
        outcome.Head.ToArray().ShouldBe(_png);
        outcome.MalwareSignature.ShouldBeNull();
    }

    /// <summary>
    /// A deployment with no scanner is what this default stands for, and it still refuses a file
    /// whose content contradicts its declaration — because that half of the inspection is a table of
    /// constants rather than a daemon.
    /// </summary>
    [Fact]
    public async Task AnSvgDepositedUnderAnImageDeclaration_IsRefusedWithNoScannerInvolved()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", _svg);

        var verdict = StoredFileContentPolicy.Decide(
            DeclaredMediaType.Create("image/png"),
            await InspectAsync(host));

        verdict.ShouldBe(ContentDecision.Quarantine);
    }

    [Fact]
    public async Task AMatchingDeposit_IsReleasedWithNoScannerInvolved()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", _png);

        var verdict = StoredFileContentPolicy.Decide(
            DeclaredMediaType.Create("image/png"),
            await InspectAsync(host));

        verdict.ShouldBe(ContentDecision.Release);
    }

    [Fact]
    public async Task AnArrangedDetection_IsReportedWithItsSignature()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", _png);
        FileContentHost.InspectionsOf(host).Infect(_key, "Win.Test.EICAR_HDB-1");

        var outcome = await InspectAsync(host);

        outcome.Status.ShouldBe(ContentInspectionStatus.Infected);
        outcome.MalwareSignature.ShouldBe("Win.Test.EICAR_HDB-1");
    }

    [Fact]
    public async Task AnObjectTheScannerRefuses_IsReportedAsNotInspectable()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", _png);
        FileContentHost.InspectionsOf(host).RefuseAsTooLarge(_key);

        (await InspectAsync(host)).Status.ShouldBe(ContentInspectionStatus.NotInspectable);
    }

    /// <summary>
    /// The outage, which the policy above must read as neither answer. It carries no head, exactly as
    /// the real adapter's does — there is none to give when the read is what failed.
    /// </summary>
    [Fact]
    public async Task AnArrangedOutage_IsReportedWithNoVerdictAndNoHead()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", _png);
        FileContentHost.InspectionsOf(host).MakeUnavailable(_key);

        var outcome = await InspectAsync(host);

        outcome.Status.ShouldBe(ContentInspectionStatus.Unavailable);
        outcome.Head.IsEmpty.ShouldBeTrue();
        StoredFileContentPolicy.Decide(DeclaredMediaType.Create("image/png"), outcome)
            .ShouldBe(ContentDecision.Retry);
    }

    /// <summary>
    /// Nothing under the key is not nothing found: reporting it as clean would release a file whose
    /// content nobody ever saw.
    /// </summary>
    [Fact]
    public async Task AnObjectThatIsNotThere_IsReportedAsNoVerdictRatherThanAsClean()
    {
        await using var host = FileContentHost.Compose();

        (await InspectAsync(host)).Status.ShouldBe(ContentInspectionStatus.Unavailable);
    }

    /// <summary>
    /// Only the prefix is kept, however large the deposit — the same bound the real adapter reads
    /// under, so a test cannot accidentally depend on seeing more of a file than production ever
    /// does.
    /// </summary>
    [Fact]
    public async Task ALargeDeposit_IsReportedWithABoundedHead()
    {
        await using var host = FileContentHost.Compose();
        byte[] content = [.. _png, .. new byte[ContentInspectionOutcome.MaxHeadBytes * 4]];
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", content);

        (await InspectAsync(host)).Head.Length.ShouldBe(ContentInspectionOutcome.MaxHeadBytes);
    }

    /// <summary>
    /// Arrangements are per key, so one file being infected says nothing about the next — and a test
    /// that infected one object cannot accidentally refuse every other.
    /// </summary>
    [Fact]
    public async Task AnArrangementReachesOnlyTheKeyItNames()
    {
        const string otherKey = "t0/202608/fedcba9876543210fedcba9876543210";
        await using var host = FileContentHost.Compose();
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", _png);
        FileContentHost.BucketOf(host).Deposit(otherKey, "image/png", _png);
        FileContentHost.InspectionsOf(host).Infect(_key, "Win.Test.EICAR_HDB-1");

        using var scope = host.CreateScope();

        (await FileContentHost.InspectorIn(scope).InspectAsync(otherKey, TestToken))
            .Status.ShouldBe(ContentInspectionStatus.Clean);
    }

    [Fact]
    public async Task Clear_ForgetsEveryArrangement()
    {
        await using var host = FileContentHost.Compose();
        FileContentHost.BucketOf(host).Deposit(_key, "image/png", _png);
        FileContentHost.InspectionsOf(host).Infect(_key, "Win.Test.EICAR_HDB-1");

        FileContentHost.InspectionsOf(host).Clear();

        (await InspectAsync(host)).Status.ShouldBe(ContentInspectionStatus.Clean);
    }

    [Fact]
    public async Task AnEmptyKey_IsARejectedArgument()
    {
        await using var host = FileContentHost.Compose();
        using var scope = host.CreateScope();

        await Should.ThrowAsync<ArgumentException>(
            () => FileContentHost.InspectorIn(scope).InspectAsync(" ", TestToken));
    }

    private static async Task<ContentInspectionOutcome> InspectAsync(IServiceProvider host)
    {
        using var scope = host.CreateScope();

        return await FileContentHost.InspectorIn(scope).InspectAsync(_key, TestToken);
    }
}
