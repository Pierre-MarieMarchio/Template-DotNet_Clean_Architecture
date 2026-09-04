using System.Globalization;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

/// <summary>
/// Walking the store, page by page, until there is nothing left.
/// </summary>
/// <remarks>
/// <para>
/// The orphan sweep reads this and deletes by difference — everything stored under a prefix that no
/// live row still names. Two failures of the walk are therefore both losses of data or of money: a
/// page boundary that skips keys leaves content nobody will ever reclaim, and a token that repeats a
/// page makes the sweep re-read the same keys for ever without reaching the end.
/// </para>
/// <para>
/// Neither can be seen without a real store. The continuation token is opaque and the store's own —
/// the adapter passes it back untouched, deliberately — so any test against a substitute is a test
/// of the substitute's idea of paging.
/// </para>
/// </remarks>
[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class ObjectInventoryTests(ObjectStoreFixture fixture)
{
    private const string _mediaType = "application/octet-stream";

    /// <summary>
    /// Seven objects over pages of two: three full pages, a partial one, and a boundary that falls
    /// between two keys rather than on the end of the set. An even count over an even page size
    /// would make the last page full, and a store that mistook "full" for "there is more" would
    /// still terminate.
    /// </summary>
    private const int _objectCount = 7;

    private const int _pageSize = 2;

    private static readonly byte[] _payload = "one small object"u8.ToArray();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListKeysAsync_CoversTheWholeStoreAcrossItsPages()
    {
        string prefix = ObjectStoreFixture.UniquePrefix("paged");
        var deposited = await DepositAsync(prefix, _objectCount);

        var walked = new List<string>();
        string? cursor = null;
        int pages = 0;

        do
        {
            var page = await fixture.Inventory.ListKeysAsync(prefix + "/", cursor, _pageSize, TestToken);

            page.Items.Count.ShouldBeLessThanOrEqualTo(_pageSize);
            page.PageSize.ShouldBe(_pageSize);

            page.TotalCount.ShouldBeNull(
                "no object store counts its own contents to answer a listing, and a page claiming a " +
                "total would be claiming a scan nobody paid for.");

            walked.AddRange(page.Items);
            cursor = page.NextCursor;
            pages++;

            // The walk is bounded by something other than the store agreeing to end it. A token the
            // store echoed unchanged would otherwise spin here for as long as the run lasts, and the
            // sweep that reads this in production would do the same against real content.
            pages.ShouldBeLessThanOrEqualTo(
                _objectCount + 1,
                $"the walk asked for a {pages}th page of {_pageSize} over {_objectCount} objects. " +
                "A listing that never says it is finished is a sweep that never finishes.");
        }
        while (cursor is not null);

        walked.ShouldBe(
            deposited,
            ignoreOrder: true,
            "every key stored under the prefix has to turn up exactly once. A key missed at a page " +
            "boundary is content the orphan sweep will never reclaim, and one seen twice is a " +
            "deletion attempted twice.");

        pages.ShouldBe(
            (_objectCount + _pageSize - 1) / _pageSize,
            "the pages are the store's, and a page short of the size asked for before the end would " +
            "mean the adapter is capping what the store returns.");
    }

    [Fact]
    public async Task ListKeysAsync_ConfinesTheWalkToItsPrefix()
    {
        string mine = ObjectStoreFixture.UniquePrefix("confined");
        string theirs = ObjectStoreFixture.UniquePrefix("someone-else");

        var deposited = await DepositAsync(mine, 2);
        await DepositAsync(theirs, 2);

        var page = await fixture.Inventory.ListKeysAsync(mine + "/", null, 100, TestToken);

        page.Items.ShouldBe(
            deposited,
            ignoreOrder: true,
            "a prefix is how a caller pays for one slice of the store rather than all of it. A walk " +
            "that ignored it would hand the orphan sweep keys belonging to a partition it was never " +
            "asked about — and the sweep deletes what it is handed.");

        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ListKeysAsync_OfAPrefixNothingIsStoredUnder_IsAnEmptyPageThatEndsTheWalk()
    {
        var page = await fixture.Inventory.ListKeysAsync(
            ObjectStoreFixture.UniquePrefix("empty") + "/",
            null,
            _pageSize,
            TestToken);

        page.Items.ShouldBeEmpty();

        page.NextCursor.ShouldBeNull(
            "the token is only meaningful on a truncated page. A cursor here would send the caller " +
            "back for a second helping of nothing, for ever.");

        page.HasNextPage.ShouldBeFalse();
    }

    /// <summary>
    /// Deposits <paramref name="count"/> objects under <paramref name="prefix"/> and answers their
    /// keys.
    /// </summary>
    /// <remarks>
    /// Through the port's own upload grants rather than through a client of this suite's own: what
    /// the inventory has to enumerate is what this application actually deposits, and a listing
    /// asserted over objects put there some other way would be a weaker claim by exactly the
    /// difference between the two.
    /// </remarks>
    private async Task<List<string>> DepositAsync(string prefix, int count)
    {
        var keys = new List<string>(count);

        for (int index = 0; index < count; index++)
        {
            // Ordinal, zero-padded, so the store's own lexicographic order is the order they were
            // created in. It makes a failure legible — a page boundary names two adjacent keys —
            // without the assertions depending on any order at all.
            string objectKey = $"{prefix}/{index.ToString("D2", CultureInfo.InvariantCulture)}";

            var grant = await fixture.Content.CreateUploadGrantAsync(
                objectKey,
                _mediaType,
                _payload.Length,
                TimeSpan.FromMinutes(10),
                TestToken);

            using var deposit = await fixture.DepositAsync(grant, _payload, TestToken);

            deposit.IsSuccessStatusCode.ShouldBeTrue(
                $"this deposit is a precondition rather than the assertion; the store answered " +
                $"{(int)deposit.StatusCode} for '{objectKey}'.");

            keys.Add(objectKey);
        }

        return keys;
    }
}
