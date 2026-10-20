using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Access.Stores;

public sealed class S3FileContentStoreTests
{
    private const string _mediaType = "image/png";

    private static readonly byte[] _bytes = Encoding.UTF8.GetBytes("the deposited bytes");

    private static readonly string _bytesChecksum = Convert.ToHexStringLower(SHA256.HashData(_bytes));

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
