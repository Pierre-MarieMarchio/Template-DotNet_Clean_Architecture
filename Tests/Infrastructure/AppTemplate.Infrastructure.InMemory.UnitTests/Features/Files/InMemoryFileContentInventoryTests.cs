using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

/// <summary>
/// The listing the orphan sweep walks. It pages, and it has to: the sweep loops until the token is
/// null and deletes what no row names, so a double that answered every listing in one page would
/// leave the loop — the part that can spin for ever or skip a page — exercised by nothing at all.
/// </summary>
public sealed class InMemoryFileContentInventoryTests
{
    private const string _mediaType = "image/png";

    private static readonly byte[] _content = [1, 2, 3];

    [Fact]
    public async Task ListKeysAsync_WalksEveryKeyOnceInOrderAcrossPages()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        Deposit(provider, "t0/202601/a", "t0/202601/b", "t0/202601/c", "t0/202601/d", "t0/202601/e");

        var inventory = FileContentHost.InventoryIn(scope);
        var walked = new List<string>();
        string? cursor = null;
        int pages = 0;

        do
        {
            var page = await inventory.ListKeysAsync("t0/", cursor, 2, TestContext.Current.CancellationToken);
            walked.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        walked.ShouldBe(["t0/202601/a", "t0/202601/b", "t0/202601/c", "t0/202601/d", "t0/202601/e"]);
        pages.ShouldBe(3);
    }

    /// <summary>
    /// The token stops on the last page. One handed back when the walk is finished is a sweep that
    /// re-reads the same tail for ever.
    /// </summary>
    [Fact]
    public async Task ListKeysAsync_EndsTheWalkOnTheLastPage()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        Deposit(provider, "t0/202601/a", "t0/202601/b");

        var page = await FileContentHost.InventoryIn(scope)
            .ListKeysAsync("t0/", null, 2, TestContext.Current.CancellationToken);

        page.Items.Count.ShouldBe(2);
        page.NextCursor.ShouldBeNull();
        page.HasNextPage.ShouldBeFalse();
    }

    /// <summary>
    /// A prefix is how a caller pays for one slice of the store rather than all of it, so a key
    /// outside it must not appear — a sweep given one prefix and answered about another would delete
    /// objects it never listed.
    /// </summary>
    [Fact]
    public async Task ListKeysAsync_AnswersOnlyAboutThePrefixItWasGiven()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        Deposit(provider, "t0/202601/a", "t1/202601/b");

        var page = await FileContentHost.InventoryIn(scope)
            .ListKeysAsync("t1/", null, 10, TestContext.Current.CancellationToken);

        page.Items.ShouldBe(["t1/202601/b"]);
    }

    /// <summary>
    /// No store counts its own contents to answer a listing, so the offset half of the page stays
    /// empty here as it does over a real one.
    /// </summary>
    [Fact]
    public async Task ListKeysAsync_ReportsNoPageNumberAndNoTotal()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();

        var page = await FileContentHost.InventoryIn(scope)
            .ListKeysAsync("t0/", null, 10, TestContext.Current.CancellationToken);

        page.Items.ShouldBeEmpty();
        page.Page.ShouldBeNull();
        page.TotalCount.ShouldBeNull();
    }

    [Fact]
    public async Task ListKeysAsync_StopsSeeingAKeyOnceItsObjectIsDeleted()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        Deposit(provider, "t0/202601/a", "t0/202601/b");

        await FileContentHost.StoreIn(scope).DeleteAsync("t0/202601/a", TestContext.Current.CancellationToken);

        var page = await FileContentHost.InventoryIn(scope)
            .ListKeysAsync("t0/", null, 10, TestContext.Current.CancellationToken);

        page.Items.ShouldBe(["t0/202601/b"]);
    }

    private static void Deposit(IServiceProvider provider, params string[] objectKeys)
    {
        var bucket = FileContentHost.BucketOf(provider);

        foreach (string objectKey in objectKeys)
        {
            bucket.Deposit(objectKey, _mediaType, _content);
        }
    }
}
