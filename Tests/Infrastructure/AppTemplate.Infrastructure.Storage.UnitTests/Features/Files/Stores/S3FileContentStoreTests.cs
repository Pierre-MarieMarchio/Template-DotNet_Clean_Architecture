using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Features.Files.Stores;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Stores;

/// <summary>
/// The calls this process makes to the bucket itself: write, read, describe, delete. The store is a
/// substitute; what a real S3-compatible store does with them is the integration suite's.
/// </summary>
public sealed class S3FileContentStoreTests
{
    private const string _mediaType = "image/png";

    private static readonly byte[] _bytes = Encoding.UTF8.GetBytes("the deposited bytes");

    private static readonly string _bytesChecksum = Convert.ToHexStringLower(SHA256.HashData(_bytes));

    /// <summary>
    /// One request carrying the length and the digest, so the store itself verifies both and either
    /// keeps the whole object or nothing.
    /// </summary>
    [Fact]
    public async Task WriteAsync_PutsTheBytesWithTheirLengthAndDigest()
    {
        var client = Substitute.For<IAmazonS3>();
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());

        var outcome = await Store(client).WriteAsync(
            StorageFixture.ObjectKey,
            new MemoryStream(_bytes),
            _mediaType,
            _bytes.Length,
            _bytesChecksum,
            TestContext.Current.CancellationToken);

        outcome.ShouldBe(ContentWriteOutcome.Written);
        await client.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(request =>
                request!.BucketName == StorageFixture.Bucket
                && request.Key == StorageFixture.ObjectKey
                && request.ContentType == _mediaType
                && request.Headers.ContentLength == _bytes.Length
                && request.ChecksumSHA256 == Convert.ToBase64String(SHA256.HashData(_bytes))
                && !request.AutoCloseStream),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A length known to be wrong is refused before anything is sent.</summary>
    [Fact]
    public async Task WriteAsync_RefusesASeekableBodyOfTheWrongLengthWithoutCallingTheStore()
    {
        var client = Substitute.For<IAmazonS3>();

        var outcome = await Store(client).WriteAsync(
            StorageFixture.ObjectKey,
            new MemoryStream(_bytes),
            _mediaType,
            _bytes.Length + 1,
            _bytesChecksum,
            TestContext.Current.CancellationToken);

        outcome.ShouldBe(ContentWriteOutcome.ContentMismatch);
        await client.DidNotReceive().PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The store's own refusal of a body that disagrees with its digest or length is an answer, not
    /// a fault: nothing was written.
    /// </summary>
    [Theory]
    [InlineData("BadDigest")]
    [InlineData("XAmzContentChecksumMismatch")]
    [InlineData("IncompleteBody")]
    public async Task WriteAsync_ReportsTheStoresRefusalOfAMismatchedBody(string errorCode)
    {
        var client = Substitute.For<IAmazonS3>();
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PutObjectResponse>>(_ =>
                throw new AmazonS3Exception("refused", ErrorType.Sender, errorCode, "request-id", HttpStatusCode.BadRequest));

        var outcome = await Store(client).WriteAsync(
            StorageFixture.ObjectKey,
            new MemoryStream(_bytes),
            _mediaType,
            _bytes.Length,
            _bytesChecksum,
            TestContext.Current.CancellationToken);

        outcome.ShouldBe(ContentWriteOutcome.ContentMismatch);
    }

    /// <summary>Any other failure is a fault and propagates.</summary>
    [Fact]
    public async Task WriteAsync_LetsAnyOtherFailurePropagate()
    {
        var client = Substitute.For<IAmazonS3>();
        client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<PutObjectResponse>>(_ =>
                throw new AmazonS3Exception("down", ErrorType.Receiver, "ServiceUnavailable", "request-id", HttpStatusCode.ServiceUnavailable));

        await Should.ThrowAsync<AmazonS3Exception>(Store(client).WriteAsync(
            StorageFixture.ObjectKey,
            new MemoryStream(_bytes),
            _mediaType,
            _bytes.Length,
            _bytesChecksum,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsTheObjectsStream()
    {
        var body = new MemoryStream(_bytes);
        var client = Substitute.For<IAmazonS3>();
        client.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectResponse { ResponseStream = body });

        var stream = await Store(client).OpenReadAsync(StorageFixture.ObjectKey, TestContext.Current.CancellationToken);

        stream.ShouldBeSameAs(body);
        await client.Received(1).GetObjectAsync(
            Arg.Is<GetObjectRequest>(request =>
                request!.BucketName == StorageFixture.Bucket && request.Key == StorageFixture.ObjectKey),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadAsync_ReturnsNullWhenNothingIsStoredUnderTheKey()
    {
        var client = Substitute.For<IAmazonS3>();
        client.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetObjectResponse>>(_ => throw NotFound());

        (await Store(client).OpenReadAsync(StorageFixture.ObjectKey, TestContext.Current.CancellationToken))
            .ShouldBeNull();
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
    /// from inside — which is the deployment <c>Files:Storage:S3:PublicEndpoint</c> exists for.
    /// </remarks>
    [Fact]
    public void TheStore_TakesOnlyTheClientThisProcessTalksToTheStoreWith()
    {
        var clients = typeof(S3FileContentStore).GetConstructors().Single().GetParameters()
            .Where(parameter => parameter.ParameterType == typeof(IAmazonS3))
            .ToList();

        clients.Count.ShouldBe(1);
        clients[0].GetCustomAttribute<FromKeyedServicesAttribute>().ShouldBeNull(
            "the presigning client is registered under a key; the store's own calls must not go through it.");
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

    private static S3FileContentStore Store(IAmazonS3 client) =>
        new(client, StorageFixture.Wrap(StorageFixture.Options()));

    private static AmazonS3Exception NotFound() =>
        new("No such key", ErrorType.Sender, "NoSuchKey", "request-id", HttpStatusCode.NotFound);
}
