using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Inventories;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Adapters.Inventories;

/// <summary>
/// The listing of the local file system store, against a real directory: ordinal order, resumable
/// from the last key seen, the same contract an S3 listing has.
/// </summary>
public sealed class FileSystemFileContentInventoryTests : IDisposable
{
    private readonly TemporaryRoot _root = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task ListKeysAsync_WalksEveryKeyOnceInOrdinalOrderAcrossPages()
    {
        // 'B' sorts before 'a' ordinally, and '2026/1' before '20260' — a culture-aware sort would not.
        string[] keys = ["t0/202608/b", "t0/202608/a", "t0/202608/B", "t0/202609/a", "t0/20260/z"];
        Create(keys);

        var walked = new List<string>();
        string? cursor = null;
        int pages = 0;

        do
        {
            var page = await Inventory().ListKeysAsync("t0/", cursor, 2, TestToken);
            walked.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        walked.ShouldBe([.. keys.Order(StringComparer.Ordinal)]);
        pages.ShouldBe(3);
    }

    /// <summary>
    /// A walk resumes strictly after the key it is given, whether that key came from the previous
    /// page or from a checkpoint saved before a restart — and whether or not it still exists.
    /// </summary>
    [Fact]
    public async Task ListKeysAsync_ResumesStrictlyAfterTheKeyItIsGiven()
    {
        Create("t0/202608/a", "t0/202608/c", "t0/202608/e");

        var page = await Inventory().ListKeysAsync("t0/", "t0/202608/b", 10, TestToken);

        page.Items.ShouldBe(["t0/202608/c", "t0/202608/e"]);
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ListKeysAsync_AnswersOnlyAboutThePrefixItWasGiven()
    {
        Create("t0/202608/a", "t1/202608/a", "t0x/202608/a");

        var page = await Inventory().ListKeysAsync("t0/", null, 10, TestToken);

        page.Items.ShouldBe(["t0/202608/a"]);
    }

    /// <summary>A write in flight is not an object, and the sweep must never reclaim one.</summary>
    [Fact]
    public async Task ListKeysAsync_SkipsTemporaryFilesOfWritesInFlight()
    {
        Create("t0/202608/a", "t0/202608/.a.0123.tmp");

        var page = await Inventory().ListKeysAsync("t0/", null, 10, TestToken);

        page.Items.ShouldBe(["t0/202608/a"]);
    }

    /// <summary>
    /// A temporary file older than any write is what a crashed write leaves behind; no key names it,
    /// so the listing reclaims it. A recent one is a write in flight and stays.
    /// </summary>
    [Fact]
    public async Task ListKeysAsync_DeletesTemporaryFilesACrashedWriteLeftBehind()
    {
        Create("t0/202608/a", "t0/202608/.b.stale.tmp", "t0/202609/.c.fresh.tmp");
        string stale = Path.Combine(_root.Path, "t0/202608/.b.stale.tmp");
        string fresh = Path.Combine(_root.Path, "t0/202609/.c.fresh.tmp");
        File.SetLastWriteTimeUtc(
            stale,
            DateTime.UtcNow - FileSystemFileContentInventory.StaleTemporaryFileAge - TimeSpan.FromMinutes(1));

        var page = await Inventory().ListKeysAsync("t0/", null, 10, TestToken);

        page.Items.ShouldBe(["t0/202608/a"]);
        File.Exists(stale).ShouldBeFalse();
        File.Exists(fresh).ShouldBeTrue();
    }

    [Fact]
    public async Task ListKeysAsync_OfARootNothingWasWrittenTo_IsAnEmptyLastPage()
    {
        var page = await Inventory().ListKeysAsync("t0/", null, 10, TestToken);

        page.Items.ShouldBeEmpty();
        page.NextCursor.ShouldBeNull();
        page.TotalCount.ShouldBeNull();
    }

    [Fact]
    public async Task ListKeysAsync_RefusesAPageSizeOfZero() =>
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Inventory().ListKeysAsync("t0/", null, 0, TestToken));

    private FileSystemFileContentInventory Inventory() => new(_root.Options);

    private void Create(params string[] keys)
    {
        foreach (string key in keys)
        {
            string path = Path.Combine(_root.Path, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
        }
    }
}
