using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.InMemory.Features.Files;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

/// <summary>
/// The double behind <see cref="IFileDirectTransfer"/>, exercised end to end through the container.
/// <para>
/// What it stands in for is a store that mints bearer rights, and the two properties worth
/// reproducing are that a grant authorises exactly one verb and that it stops working. Both are
/// asserted by verifying the grant rather than by reading the string it is made of, because a
/// signature nothing verifies is decoration and an expiry nothing checks is a comment.
/// </para>
/// </summary>
public sealed class InMemoryFileDirectTransferTests
{
    private const string _objectKey = "t0/202601/9f2c1d7a4b6e8f0132547698badcfe10";

    private const string _mediaType = "image/png";

    private static readonly byte[] _content = Encoding.UTF8.GetBytes("the deposited bytes");

    /// <summary>
    /// The digest of <see cref="_content"/>, computed rather than written down: a grant binds it, so a
    /// constant that drifted from the bytes would mint a grant no honest deposit could satisfy.
    /// </summary>
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

    /// <summary>
    /// A grant covers one verb. A right to deposit that also authorised a read would let a client
    /// that was given an upload URL read whatever ends up under the key.
    /// </summary>
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

    /// <summary>
    /// The same two headers the S3 adapter's signature covers, so a client written against the double
    /// sends what the real one will require of it.
    /// </summary>
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

    /// <summary>
    /// The host is reserved by RFC 2606 and can never resolve. A test that follows a grant fails at
    /// DNS instead of reaching something real, which is the only honest thing a double can promise
    /// about a URL it did not put any bytes behind.
    /// </summary>
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

    /// <summary>
    /// Issuing a grant is not depositing. The case where a client is handed an upload URL and never
    /// uses it is exactly what confirmation and the abandonment sweep exist for, so a double that
    /// wrote the object when the grant was minted would make it untestable.
    /// </summary>
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
