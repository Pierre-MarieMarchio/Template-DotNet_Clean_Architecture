using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Buckets;
using AppTemplate.Infrastructure.Storage.Objects;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Objects;

/// <summary>
/// What a grant this adapter mints actually authorises, and what it says it authorises. The two are
/// separate assertions on purpose: a grant that told a client to send headers the signature does not
/// cover, or covered headers it did not mention, would fail every upload with a signature error that
/// names none of them.
/// </summary>
public sealed class S3FileContentStoreTests
{
    private const string _mediaType = "image/png";

    private const long _size = 4096;

    /// <summary>
    /// The property the whole upload path rests on: the grant's <c>RequiredHeaders</c> is exactly the
    /// signed set, minus <c>host</c>, which the client does not choose. Read off the URL rather than
    /// off the request, because the signature is the only thing the store will consult.
    /// </summary>
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

    /// <summary>
    /// The two facts the client declared are bound into the signature, so a deposit that changes
    /// either is refused by the store with nothing written — rather than at confirmation, with the
    /// wrong bytes already there.
    /// </summary>
    [Fact]
    public async Task CreateUploadGrantAsync_BindsTheDeclaredTypeAndSizeIntoTheGrant()
    {
        var grant = await UploadGrantAsync();

        grant.Method.ShouldBe("PUT");
        grant.RequiredHeaders["Content-Type"].ShouldBe(_mediaType);
        grant.RequiredHeaders["Content-Length"].ShouldBe("4096");
    }

    /// <summary>
    /// The deposit is made to record a SHA-256 of the bytes as it writes them. It is the only fact in
    /// the feature the client does not author, and <c>DescribeAsync</c> has nowhere else to get one:
    /// this adapter deliberately never reads an object to hash it.
    /// </summary>
    [Fact]
    public async Task CreateUploadGrantAsync_MakesTheDepositRecordASha256()
    {
        var grant = await UploadGrantAsync();

        grant.RequiredHeaders["x-amz-sdk-checksum-algorithm"].ShouldBe("SHA256");
    }

    /// <summary>
    /// A lifetime longer than the operator's ceiling is shortened, and the grant says so: the instant
    /// handed back is computed from what was signed, so a caller is never told a URL works for longer
    /// than the store will honour it.
    /// </summary>
    [Fact]
    public async Task CreateUploadGrantAsync_ClampsALifetimeLongerThanTheConfiguredCeiling()
    {
        var options = StorageFixture.Options(storage => storage.MaxGrantLifetime = TimeSpan.FromMinutes(10));
        var before = DateTimeOffset.UtcNow;

        var grant = await Store(options).CreateUploadGrantAsync(
            StorageFixture.ObjectKey,
            _mediaType,
            _size,
            TimeSpan.FromHours(6),
            TestContext.Current.CancellationToken);

        new SignedUrl(grant.Url).Lifetime.ShouldBe(TimeSpan.FromMinutes(10));
        grant.ExpiresAt.ShouldBeInRange(before.AddMinutes(10), DateTimeOffset.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task CreateUploadGrantAsync_SignsTheLifetimeItWasAskedFor()
    {
        var grant = await UploadGrantAsync(TimeSpan.FromMinutes(3));

        new SignedUrl(grant.Url).Lifetime.ShouldBe(TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// The download is offered as an attachment under the name the user chose, twice: a quoted ASCII
    /// form and the RFC 6266 <c>filename*</c> that carries the real one. The overrides are signed
    /// query parameters, so the client cannot turn the attachment into something the browser renders.
    /// </summary>
    [Fact]
    public async Task CreateDownloadGrantAsync_OffersTheFileAsAnAttachmentUnderItsOwnName()
    {
        var grant = await Store(StorageFixture.Options()).CreateDownloadGrantAsync(
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

    /// <summary>
    /// The scheme follows the endpoint URLs are signed for, and it has to be made to: the SDK signs an
    /// HTTPS URL whatever the endpoint says, so a development store reached over plain HTTP would be
    /// handed addresses it does not answer on — and the signature covers the host, so no client could
    /// correct it.
    /// </summary>
    /// <remarks>
    /// The last two rows are the deployment this module is shaped for and the one nothing used to
    /// handle: plain HTTP inside a mesh with TLS at the ingress mints <c>https</c>, and its reverse
    /// mints <c>http</c>. Both used to read the internal endpoint and get the wrong answer.
    /// </remarks>
    [Theory]
    [InlineData("http://minio:9000", "", "http")]
    [InlineData("https://objects.example", "", "https")]
    [InlineData("", "", "https")]
    [InlineData("http://minio:9000", "https://files.example", "https")]
    [InlineData("https://minio.internal", "http://minio:9000", "http")]
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

        var grant = await Store(options).CreateDownloadGrantAsync(
            StorageFixture.ObjectKey,
            "report.png",
            _mediaType,
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        new SignedUrl(grant.Url).Uri.Scheme.ShouldBe(expected);
    }

    /// <summary>
    /// The host in a grant is the public one, because it is the only one the client following it can
    /// resolve — and the signature covers <c>host</c>, so it cannot be corrected downstream by the
    /// API, by a proxy or by the client.
    /// </summary>
    [Theory]
    [InlineData("upload")]
    [InlineData("download")]
    public async Task AGrant_NamesThePublicEndpointRatherThanTheOneThisProcessTalksTo(string kind)
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://minio:9000";
            storage.PublicEndpoint = "https://files.example";
            storage.ForcePathStyle = true;
        });

        string url = kind == "upload"
            ? (await Store(options).CreateUploadGrantAsync(
                StorageFixture.ObjectKey,
                _mediaType,
                _size,
                TimeSpan.FromMinutes(5),
                TestContext.Current.CancellationToken)).Url
            : (await Store(options).CreateDownloadGrantAsync(
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

    /// <summary>
    /// No public endpoint is the configuration every deployment predating this setting has, and it
    /// signs for exactly what it signed for before — the same host, the same scheme, the same
    /// address style.
    /// </summary>
    [Fact]
    public async Task AGrant_NamesTheEndpointItselfWhenNoPublicOneIsConfigured()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "https://objects.example";
            storage.ForcePathStyle = true;
        });

        var grant = await Store(options).CreateDownloadGrantAsync(
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
    public async Task DescribeAsync_ReturnsNullWhenNothingIsStoredUnderTheKey()
    {
        var client = Substitute.For<IAmazonS3>();
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetObjectMetadataResponse>>(_ => throw NotFound());

        var description = await Store(client).DescribeAsync(
            StorageFixture.ObjectKey,
            TestContext.Current.CancellationToken);

        description.ShouldBeNull();
    }

    /// <summary>
    /// The store reports its digest in base64 and the port asks for lower-case hexadecimal. They are
    /// the same 32 bytes written differently, and comparing the two encodings as strings would fail
    /// every confirmation with a mismatch that explains nothing.
    /// </summary>
    [Fact]
    public async Task DescribeAsync_ReportsTheStoresOwnDigestAsHexadecimal()
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("the deposited bytes"));

        var client = Substitute.For<IAmazonS3>();
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse
            {
                ContentLength = 19,
                ChecksumSHA256 = Convert.ToBase64String(digest),
            });

        var description = await Store(client).DescribeAsync(
            StorageFixture.ObjectKey,
            TestContext.Current.CancellationToken);

        description.ShouldBe(new StoredObjectDescription(19, Convert.ToHexStringLower(digest)));
    }

    /// <summary>
    /// An object with no recorded digest is a fault and is reported as one. Answering with an entity
    /// tag — which is an MD5, or a digest of digests, or opaque — would fail confirmation for a
    /// reason no message could name.
    /// </summary>
    [Fact]
    public async Task DescribeAsync_RefusesAnObjectTheStoreRecordedNoDigestFor()
    {
        var client = Substitute.For<IAmazonS3>();
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ContentLength = 19, ChecksumSHA256 = null });

        var describing = Store(client).DescribeAsync(
            StorageFixture.ObjectKey,
            TestContext.Current.CancellationToken);

        var failure = await Should.ThrowAsync<InvalidOperationException>(describing);
        failure.Message.ShouldContain(StorageFixture.ObjectKey);
    }

    [Fact]
    public async Task DescribeAsync_AsksAboutTheConfiguredBucket()
    {
        var client = Substitute.For<IAmazonS3>();
        client.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse
            {
                ContentLength = 1,
                ChecksumSHA256 = Convert.ToBase64String(SHA256.HashData([1])),
            });

        await Store(client).DescribeAsync(StorageFixture.ObjectKey, TestContext.Current.CancellationToken);

        await client.Received(1).GetObjectMetadataAsync(
            Arg.Is<GetObjectMetadataRequest>(request =>
                request!.BucketName == StorageFixture.Bucket
                && request.Key == StorageFixture.ObjectKey
                && request.ChecksumMode == ChecksumMode.ENABLED),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The calls this process makes itself go over the endpoint this process can reach, never over
    /// the public one.
    /// </summary>
    /// <remarks>
    /// The two clients differ by host name alone, so wiring the wrong one in compiles, passes every
    /// assertion about a grant, and fails only in a deployment where the public name does not resolve
    /// from inside — which is the deployment <c>Storage:PublicEndpoint</c> exists for.
    /// </remarks>
    [Fact]
    public async Task TheCallsThisProcessMakes_GoThroughTheClientItTalksToTheStoreWith()
    {
        var client = Substitute.For<IAmazonS3>();
        var signer = Substitute.For<IAmazonS3>();

        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse());

        var store = new S3FileContentStore(client, signer, StorageFixture.Wrap(StorageFixture.Options()));

        await store.DeleteAsync(StorageFixture.ObjectKey, TestContext.Current.CancellationToken);

        await client.Received(1).DeleteObjectAsync(
            Arg.Any<DeleteObjectRequest>(),
            Arg.Any<CancellationToken>());

        await signer.DidNotReceive().DeleteObjectAsync(
            Arg.Any<DeleteObjectRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Both the reaction to a deletion and the orphan sweep reach the same key, and neither is
    /// coordinated with the other — so the second one must not fail.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_SucceedsWhenTheObjectIsAlreadyGone()
    {
        var client = Substitute.For<IAmazonS3>();
        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<DeleteObjectResponse>>(_ => throw NotFound());

        await Should.NotThrowAsync(Store(client).DeleteAsync(
            StorageFixture.ObjectKey,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheKeyFromTheConfiguredBucket()
    {
        var client = Substitute.For<IAmazonS3>();
        client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteObjectResponse());

        await Store(client).DeleteAsync(StorageFixture.ObjectKey, TestContext.Current.CancellationToken);

        await client.Received(1).DeleteObjectAsync(
            Arg.Is<DeleteObjectRequest>(request =>
                request!.BucketName == StorageFixture.Bucket && request.Key == StorageFixture.ObjectKey),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateUploadGrantAsync_RefusesALifetimeThatHasAlreadyPassed()
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(Store(StorageFixture.Options())
            .CreateUploadGrantAsync(
                StorageFixture.ObjectKey,
                _mediaType,
                _size,
                TimeSpan.Zero,
                TestContext.Current.CancellationToken));
    }

    private static Task<IssuedUploadGrant> UploadGrantAsync(TimeSpan? lifetime = null) =>
        Store(StorageFixture.Options()).CreateUploadGrantAsync(
            StorageFixture.ObjectKey,
            _mediaType,
            _size,
            lifetime ?? TimeSpan.FromMinutes(30),
            TestContext.Current.CancellationToken);

    private static S3FileContentStore Store(StorageOptions options) =>
        new(StorageFixture.Client(options), StorageFixture.Signer(options), StorageFixture.Wrap(options));

    /// <summary>
    /// The signing client is a substitute nothing configures, deliberately: every assertion made
    /// through this overload is about a call to the store, so an adapter that made one through the
    /// presigning client would answer with nothing rather than pass quietly.
    /// </summary>
    private static S3FileContentStore Store(IAmazonS3 client) =>
        new(client, Substitute.For<IAmazonS3>(), StorageFixture.Wrap(StorageFixture.Options()));

    private static AmazonS3Exception NotFound() =>
        new("No such key", ErrorType.Sender, "NoSuchKey", "request-id", HttpStatusCode.NotFound);
}
