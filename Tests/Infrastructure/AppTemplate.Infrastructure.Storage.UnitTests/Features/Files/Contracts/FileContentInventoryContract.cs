using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Contracts;

public abstract class FileContentInventoryContract
{
    private const string _mediaType = "application/octet-stream";

    private static readonly byte[] _bytes = "one small object"u8.ToArray();

    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_bytes));

    protected static CancellationToken TestToken => TestContext.Current.CancellationToken;

    protected abstract IFileContentStore Store { get; }

    protected abstract IFileContentInventory Inventory { get; }

    protected virtual string NewPrefix() => $"t{Guid.CreateVersion7():N}";

    [Fact]
    public async Task ListKeysAsync_WalksEveryKeyOnceInOrdinalOrderAcrossPages()
    {
        string prefix = NewPrefix();
        string[] keys = await StoreAsync(prefix, "202608/b", "202608/a", "202608/_b", "202609/a", "20260/z");

        var walked = new List<string>();
        string? cursor = null;
        int pages = 0;

        do
        {
            var page = await Inventory.ListKeysAsync(prefix + "/", cursor, 2, TestToken);

            page.Items.Count.ShouldBeLessThanOrEqualTo(2);
            page.Page.ShouldBeNull();
            page.TotalCount.ShouldBeNull("no store counts its contents to answer a listing.");

            walked.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;

            pages.ShouldBeLessThanOrEqualTo(keys.Length + 1, "a listing that never says it is finished is a sweep that never ends.");
        }
        while (cursor is not null);

        walked.ShouldBe([.. keys.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task ListKeysAsync_ResumesStrictlyAfterTheKeyItIsGiven()
    {
        string prefix = NewPrefix();
        await StoreAsync(prefix, "202608/a", "202608/c", "202608/e");

        var page = await Inventory.ListKeysAsync(prefix + "/", $"{prefix}/202608/b", 10, TestToken);

        page.Items.ShouldBe([$"{prefix}/202608/c", $"{prefix}/202608/e"]);
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ListKeysAsync_AnswersOnlyAboutThePrefixItWasGiven()
    {
        string prefix = NewPrefix();
        await StoreAsync(prefix, "202608/a");
        await StoreAsync(prefix + "x", "202608/a");

        var page = await Inventory.ListKeysAsync(prefix + "/", null, 10, TestToken);

        page.Items.ShouldBe([$"{prefix}/202608/a"]);
    }

    [Fact]
    public async Task ListKeysAsync_StopsSeeingAKeyOnceItsObjectIsDeleted()
    {
        string prefix = NewPrefix();
        string[] keys = await StoreAsync(prefix, "202608/a", "202608/b");

        await Store.DeleteAsync(keys[0], TestToken);

        (await Inventory.ListKeysAsync(prefix + "/", null, 10, TestToken)).Items.ShouldBe([keys[1]]);
    }

    [Fact]
    public async Task ListKeysAsync_OfAPrefixNothingIsStoredUnder_IsAnEmptyLastPage()
    {
        var page = await Inventory.ListKeysAsync(NewPrefix() + "/", null, 10, TestToken);

        page.Items.ShouldBeEmpty();
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ListKeysAsync_RefusesAPageSizeOfZero() =>
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => Inventory.ListKeysAsync(NewPrefix() + "/", null, 0, TestToken));

    private async Task<string[]> StoreAsync(string prefix, params string[] names)
    {
        string[] keys = [.. names.Select(name => $"{prefix}/{name}")];

        foreach (string key in keys)
        {
            (await Store.WriteAsync(key, new MemoryStream(_bytes), _mediaType, _bytes.Length, _checksum, TestToken))
                .ShouldBe(ContentWriteOutcome.Written);
        }

        return keys;
    }
}
