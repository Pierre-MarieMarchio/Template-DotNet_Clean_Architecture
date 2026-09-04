using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Infrastructure.Storage.Features.Files.Inventories;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Inventories;

/// <summary>
/// The listing the orphan sweep walks. What matters here is the token: the sweep loops until it is
/// null, so a page that hands one back when the walk is finished is an infinite loop, and one that
/// drops it when the walk is not finished silently stops reclaiming anything past the first page.
/// </summary>
public sealed class S3FileContentInventoryTests
{
    private const string _prefix = "t0/";

    /// <summary>
    /// The listing is a call this process makes, so it goes over the endpoint this process can reach
    /// and never over the public one.
    /// </summary>
    /// <remarks>
    /// The two clients differ by host name alone. Handing this one the presigning client would
    /// compile, would satisfy every assertion below — the substitute answers either way — and would
    /// fail only in a deployment where the public name does not resolve from inside, which is the
    /// deployment <c>Storage:PublicEndpoint</c> exists for. What is checked is therefore the
    /// constructor: one client, asked for without a key.
    /// </remarks>
    [Fact]
    public void TheInventory_TakesOnlyTheClientThisProcessTalksToTheStoreWith()
    {
        var parameters = typeof(S3FileContentInventory).GetConstructors().Single().GetParameters();

        var clients = parameters.Where(parameter => parameter.ParameterType == typeof(IAmazonS3)).ToList();

        clients.Count.ShouldBe(1);
        clients[0].GetCustomAttribute<FromKeyedServicesAttribute>().ShouldBeNull(
            "the presigning client is registered under a key, so asking for one by key here would be " +
            "the listing walking a store this process may not be able to reach.");
    }

    [Fact]
    public async Task ListKeysAsync_CarriesTheStoresOwnTokenWhileMoreRemains()
    {
        var inventory = Inventory(Page(["t0/202608/a", "t0/202608/b"], truncated: true, next: "opaque-token"));

        var page = await inventory.ListKeysAsync(_prefix, null, 2, TestContext.Current.CancellationToken);

        page.Items.ShouldBe(["t0/202608/a", "t0/202608/b"]);
        page.NextCursor.ShouldBe("opaque-token");
        page.HasNextPage.ShouldBeTrue();
    }

    /// <summary>
    /// A store that echoes its token on the last page would otherwise put the sweep in a loop that
    /// never ends and re-reads the same page for ever, so truncation decides rather than the token's
    /// presence.
    /// </summary>
    [Fact]
    public async Task ListKeysAsync_EndsTheWalkWhenTheListingIsNotTruncated()
    {
        var inventory = Inventory(Page(["t0/202608/a"], truncated: false, next: "echoed-token"));

        var page = await inventory.ListKeysAsync(_prefix, "previous", 2, TestContext.Current.CancellationToken);

        page.NextCursor.ShouldBeNull();
        page.HasNextPage.ShouldBeFalse();
    }

    /// <summary>
    /// No store counts its own contents to answer a listing, so the offset half of the page stays
    /// empty rather than being filled with a number computed here.
    /// </summary>
    [Fact]
    public async Task ListKeysAsync_ReportsNoPageNumberAndNoTotal()
    {
        var page = await Inventory(Page([], truncated: false, next: null))
            .ListKeysAsync(_prefix, null, 10, TestContext.Current.CancellationToken);

        page.Items.ShouldBeEmpty();
        page.Page.ShouldBeNull();
        page.TotalCount.ShouldBeNull();
        page.PageSize.ShouldBe(10);
    }

    [Fact]
    public async Task ListKeysAsync_AsksTheConfiguredBucketForOnePageUnderThePrefix()
    {
        var client = Client(Page([], truncated: false, next: null));

        await Inventory(client).ListKeysAsync(_prefix, "resume-here", 500, TestContext.Current.CancellationToken);

        await client.Received(1).ListObjectsV2Async(
            Arg.Is<ListObjectsV2Request>(request =>
                request!.BucketName == StorageFixture.Bucket
                && request.Prefix == _prefix
                && request.MaxKeys == 500
                && request.ContinuationToken == "resume-here"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListKeysAsync_RefusesAPageSizeOfZero()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            Inventory(Page([], truncated: false, next: null))
                .ListKeysAsync(_prefix, null, 0, TestContext.Current.CancellationToken));
    }

    private static ListObjectsV2Response Page(string[] keys, bool truncated, string? next) =>
        new()
        {
            S3Objects = [.. keys.Select(key => new S3Object { Key = key })],
            IsTruncated = truncated,
            NextContinuationToken = next,
        };

    private static IAmazonS3 Client(ListObjectsV2Response response)
    {
        var client = Substitute.For<IAmazonS3>();
        client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(response);

        return client;
    }

    private static S3FileContentInventory Inventory(ListObjectsV2Response response) => Inventory(Client(response));

    private static S3FileContentInventory Inventory(IAmazonS3 client) =>
        new(client, StorageFixture.Wrap(StorageFixture.Options()));
}
