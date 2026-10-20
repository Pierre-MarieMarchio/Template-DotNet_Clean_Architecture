using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

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

    [Fact]
    public async Task ListKeysAsync_ResumesStrictlyAfterTheKeyItIsGiven()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        Deposit(provider, "t0/202601/a", "t0/202601/b", "t0/202601/c");

        var page = await FileContentHost.InventoryIn(scope)
            .ListKeysAsync("t0/", "t0/202601/a", 10, TestContext.Current.CancellationToken);

        page.Items.ShouldBe(["t0/202601/b", "t0/202601/c"]);
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
