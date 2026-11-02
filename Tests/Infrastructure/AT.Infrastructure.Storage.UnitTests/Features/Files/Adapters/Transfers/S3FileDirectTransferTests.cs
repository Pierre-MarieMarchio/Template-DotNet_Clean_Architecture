using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Transfers;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Adapters.Transfers;

public sealed class S3FileDirectTransferTests
{
    private const string _mediaType = "image/png";

    private const long _size = 4096;

    private const string _checksum = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private static readonly TimeSpan _clockSlack = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task CreateUploadGrantAsync_RequiresExactlyTheHeadersItsSignatureCovers()
    {
        var grant = await UploadGrantAsync();

        var signed = new SignedUrl(grant.Url).SignedHeaders;

        signed.ShouldContain("host");
        signed.Where(header => header != "host")
            .Order(StringComparer.Ordinal)
            .ShouldBe(grant.RequiredHeaders.Keys.Select(header => header.ToLowerInvariant()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CreateUploadGrantAsync_BindsTheDeclaredTypeAndSizeIntoTheGrant()
    {
        var grant = await UploadGrantAsync();

        grant.Method.ShouldBe("PUT");
        grant.RequiredHeaders["Content-Type"].ShouldBe(_mediaType);
        grant.RequiredHeaders["Content-Length"].ShouldBe("4096");
    }

    [Fact]
    public async Task CreateUploadGrantAsync_BindsTheDigestItselfRatherThanTheAlgorithmsName()
    {
        var grant = await UploadGrantAsync();

        grant.RequiredHeaders.ShouldNotContainKey(
            "x-amz-sdk-checksum-algorithm",
            "naming the algorithm without its value is what left every deposit undescribable.");

        grant.RequiredHeaders["x-amz-checksum-sha256"].ShouldBe(
            Convert.ToBase64String(Convert.FromHexString(_checksum)),
            "the store wants base64 of the raw digest where the domain carries hexadecimal, and the " +
            "grant is signed over whichever form is sent — so getting this wrong mints a grant no " +
            "honest deposit can satisfy.");
    }

    [Fact]
    public async Task CreateUploadGrantAsync_ClampsALifetimeLongerThanTheConfiguredCeiling()
    {
        var options = StorageFixture.Options(storage => storage.MaxGrantLifetime = TimeSpan.FromMinutes(10));
        var before = DateTimeOffset.UtcNow;

        var grant = await Transfer(options).CreateUploadGrantAsync(
            StorageFixture.ObjectKey,
            _mediaType,
            _size,
            _checksum,
            TimeSpan.FromHours(6),
            TestContext.Current.CancellationToken);

        var granted = TimeSpan.FromMinutes(10);
        new SignedUrl(grant.Url).Lifetime.ShouldBeInRange(granted - _clockSlack, granted + _clockSlack);
        grant.ExpiresAt.ShouldBeInRange(before + granted - _clockSlack, DateTimeOffset.UtcNow + granted + _clockSlack);
    }

    [Fact]
    public async Task CreateUploadGrantAsync_SignsTheLifetimeItWasAskedFor()
    {
        var grant = await UploadGrantAsync(TimeSpan.FromMinutes(3));

        var granted = TimeSpan.FromMinutes(3);
        new SignedUrl(grant.Url).Lifetime.ShouldBeInRange(granted - _clockSlack, granted + _clockSlack);
    }

    [Fact]
    public async Task CreateDownloadGrantAsync_OffersTheFileAsAnAttachmentUnderItsOwnName()
    {
        var grant = await Transfer(StorageFixture.Options()).CreateDownloadGrantAsync(
            StorageFixture.ObjectKey,
            "rapport été.png",
            _mediaType,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        string? disposition = new SignedUrl(grant.Url).Parameter("response-content-disposition");

        disposition.ShouldNotBeNull();
        disposition.ShouldStartWith("attachment; ");
        disposition.ShouldContain("filename=\"rapport _t_.png\"");
        disposition.ShouldContain("filename*=UTF-8''rapport%20%C3%A9t%C3%A9.png");
    }

    [Theory]
    [InlineData("http://objectstore:9000", "", "http")]
    [InlineData("https://objects.example", "", "https")]
    [InlineData("", "", "https")]
    [InlineData("http://objectstore:9000", "https://files.example", "https")]
    [InlineData("https://objectstore.internal", "http://objectstore:9000", "http")]
    public async Task CreateDownloadGrantAsync_MintsAUrlOnTheSigningEndpointsOwnScheme(
        string endpoint,
        string publicEndpoint,
        string expected)
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = endpoint;
            storage.PublicEndpoint = publicEndpoint;
            storage.AllowInsecureTransport = true;
        });

        var grant = await Transfer(options).CreateDownloadGrantAsync(
            StorageFixture.ObjectKey,
            "report.png",
            _mediaType,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        new SignedUrl(grant.Url).Uri.Scheme.ShouldBe(expected);
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("download")]
    public async Task AGrant_NamesThePublicEndpointRatherThanTheOneThisProcessTalksTo(string kind)
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://objectstore:9000";
            storage.PublicEndpoint = "https://files.example";
            storage.ForcePathStyle = true;
        });

        string url = kind == "upload"
            ? (await Transfer(options).CreateUploadGrantAsync(
                StorageFixture.ObjectKey,
                _mediaType,
                _size,
                _checksum,
                TimeSpan.FromMinutes(5),
                TestContext.Current.CancellationToken)).Url
            : (await Transfer(options).CreateDownloadGrantAsync(
                StorageFixture.ObjectKey,
                "report.png",
                _mediaType,
                TimeSpan.FromMinutes(5),
                TestContext.Current.CancellationToken)).Url;

        var signed = new SignedUrl(url);

        signed.Uri.Host.ShouldBe("files.example");
        signed.SignedHeaders.ShouldContain(
            "host",
            "the host being inside the signature is the whole reason this setting exists: a URL " +
            "signed for one name and followed under another is refused, so rewriting it afterwards " +
            "is not an option anyone has.");
    }

    [Fact]
    public async Task AGrant_NamesTheEndpointItselfWhenNoPublicOneIsConfigured()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "https://objects.example";
            storage.ForcePathStyle = true;
        });

        var grant = await Transfer(options).CreateDownloadGrantAsync(
            StorageFixture.ObjectKey,
            "report.png",
            _mediaType,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        var signed = new SignedUrl(grant.Url).Uri;

        signed.Host.ShouldBe("objects.example");
        signed.Scheme.ShouldBe("https");
        signed.AbsolutePath.ShouldBe($"/{StorageFixture.Bucket}/{StorageFixture.ObjectKey}");
    }

    [Fact]
    public void IsSupported_IsTrue() =>
        Transfer(StorageFixture.Options()).IsSupported.ShouldBeTrue();

    [Fact]
    public async Task CreateUploadGrantAsync_RefusesALifetimeThatHasAlreadyPassed()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(Transfer(StorageFixture.Options())
            .CreateUploadGrantAsync(
                StorageFixture.ObjectKey,
                _mediaType,
                _size,
                _checksum,
                TimeSpan.Zero,
                TestContext.Current.CancellationToken));
    }

    private static Task<IssuedUploadGrant> UploadGrantAsync(TimeSpan? lifetime = null) =>
        Transfer(StorageFixture.Options()).CreateUploadGrantAsync(
            StorageFixture.ObjectKey,
            _mediaType,
            _size,
            _checksum,
            lifetime ?? TimeSpan.FromMinutes(30),
            TestContext.Current.CancellationToken);

    private static S3FileDirectTransfer Transfer(FilesStorageS3Options options) =>
        new(StorageFixture.Signer(options), StorageFixture.Wrap(options));
}
