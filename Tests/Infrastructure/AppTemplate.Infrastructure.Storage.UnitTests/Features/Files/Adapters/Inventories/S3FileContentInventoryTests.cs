using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Inventories;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Adapters.Inventories;

public sealed class S3FileContentInventoryTests
{
    private const string _prefix = "t0/";

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
    public async Task ListKeysAsync_CarriesTheLastKeyWhileMoreRemain()
    {
        var inventory = Inventory(Page(["t0/202608/a", "t0/202608/b"], truncated: true, next: "opaque-token"));

        var page = await inventory.ListKeysAsync(_prefix, null, 2, TestContext.Current.CancellationToken);

        page.Items.ShouldBe(["t0/202608/a", "t0/202608/b"]);
        page.NextCursor.ShouldBe("t0/202608/b");
        page.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task ListKeysAsync_EndsTheWalkOnAnEmptyPageEvenIfTruncated()
    {
        var page = await Inventory(Page([], truncated: true, next: "opaque-token"))
            .ListKeysAsync(_prefix, null, 2, TestContext.Current.CancellationToken);

        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task ListKeysAsync_EndsTheWalkWhenTheListingIsNotTruncated()
    {
        var inventory = Inventory(Page(["t0/202608/a"], truncated: false, next: "echoed-token"));

        var page = await inventory.ListKeysAsync(_prefix, "previous", 2, TestContext.Current.CancellationToken);

        page.NextCursor.ShouldBeNull();
        page.HasNextPage.ShouldBeFalse();
    }

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

        await Inventory(client).ListKeysAsync(_prefix, "t0/202608/resume-here", 500, TestContext.Current.CancellationToken);

        await client.Received(1).ListObjectsV2Async(
            Arg.Is<ListObjectsV2Request>(request =>
                request!.BucketName == StorageFixture.Bucket
                && request.Prefix == _prefix
                && request.MaxKeys == 500
                && request.StartAfter == "t0/202608/resume-here"
                && request.ContinuationToken == null),
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
