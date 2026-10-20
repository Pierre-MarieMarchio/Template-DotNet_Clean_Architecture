using System.Security.Cryptography;
using System.Text;
using AppTemplate.Infrastructure.InMemory.Features.Files;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

public sealed class InMemoryFileDirectTransferTests
{
    private const string _objectKey = "t0/202601/9f2c1d7a4b6e8f0132547698badcfe10";

    private const string _mediaType = "image/png";

    private static readonly byte[] _content = Encoding.UTF8.GetBytes("the deposited bytes");

    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_content));

    [Fact]
    public async Task CreateUploadGrantAsync_MintsAWriteRightThatExpires()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        var clock = FileContentHost.ClockOf(provider);
        var bucket = FileContentHost.BucketOf(provider);

        var grant = await FileContentHost.TransferIn(scope).CreateUploadGrantAsync(
            _objectKey,
            _mediaType,
            _content.Length,
            _checksum,
            TimeSpan.FromMinutes(30),
            TestContext.Current.CancellationToken);

        grant.Method.ShouldBe("PUT");
        grant.ExpiresAt.ShouldBe(clock.UtcNow.AddMinutes(30));
        bucket.IsGrantValid(grant.Url, "PUT", clock.UtcNow).ShouldBeTrue();

        clock.Advance(TimeSpan.FromMinutes(31));
        bucket.IsGrantValid(grant.Url, "PUT", clock.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateUploadGrantAsync_AuthorisesNothingButTheDeposit()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();

        var grant = await FileContentHost.TransferIn(scope).CreateUploadGrantAsync(
            _objectKey,
            _mediaType,
            _content.Length,
            _checksum,
            TimeSpan.FromMinutes(30),
            TestContext.Current.CancellationToken);

        FileContentHost.BucketOf(provider)
            .IsGrantValid(grant.Url, "GET", FileContentHost.ClockOf(provider).UtcNow)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task CreateUploadGrantAsync_RequiresTheDeclaredTypeAndSize()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();

        var grant = await FileContentHost.TransferIn(scope).CreateUploadGrantAsync(
            _objectKey,
            _mediaType,
            4096,
            _checksum,
            TimeSpan.FromMinutes(30),
            TestContext.Current.CancellationToken);

        grant.RequiredHeaders["Content-Type"].ShouldBe(_mediaType);
        grant.RequiredHeaders["Content-Length"].ShouldBe("4096");
    }

    [Fact]
    public async Task CreateDownloadGrantAsync_MintsAReadRightThatExpires()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();
        var clock = FileContentHost.ClockOf(provider);

        var grant = await FileContentHost.TransferIn(scope).CreateDownloadGrantAsync(
            _objectKey,
            "rapport été.png",
            _mediaType,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        grant.ExpiresAt.ShouldBe(clock.UtcNow.AddMinutes(5));
        FileContentHost.BucketOf(provider).IsGrantValid(grant.Url, "GET", clock.UtcNow).ShouldBeTrue();
        FileContentHost.BucketOf(provider).IsGrantValid(grant.Url, "PUT", clock.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public async Task EveryGrant_PointsAtAHostThatCannotExist()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();

        var grant = await FileContentHost.TransferIn(scope).CreateDownloadGrantAsync(
            _objectKey,
            "report.png",
            _mediaType,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        new Uri(grant.Url).Host.ShouldEndWith(".invalid");
        grant.Url.ShouldStartWith(StoredObjects.Origin);
    }

    [Fact]
    public void IsSupported_IsTrue()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();

        FileContentHost.TransferIn(scope).IsSupported.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUploadGrant_StoresNothingUntilADepositHappens()
    {
        using var provider = FileContentHost.Compose();
        using var scope = provider.CreateScope();

        await FileContentHost.TransferIn(scope).CreateUploadGrantAsync(
            _objectKey,
            _mediaType,
            _content.Length,
            _checksum,
            TimeSpan.FromMinutes(30),
            TestContext.Current.CancellationToken);

        (await FileContentHost.StoreIn(scope).DescribeAsync(_objectKey, TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }
}
