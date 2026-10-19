using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Common.Budgets;
using AppTemplate.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;

/// <summary>
/// <see cref="IFileContentStore"/> over an S3-compatible bucket; signed URLs are
/// <c>S3FileDirectTransfer</c>'s. The SDK escapes the outbound HTTP policy, so <see cref="BucketBudget"/>
/// restates it.
/// </summary>
internal sealed class S3FileContentStore(IAmazonS3 client, IOptions<FilesStorageS3Options> options) : IFileContentStore
{
    private const int _sha256DigestLength = 32;

    /// <summary>Codes answered when a body disagrees with its declared length or digest; nothing is written.</summary>
    private static readonly HashSet<string> _contentMismatchCodes = new(StringComparer.Ordinal)
    {
        "BadDigest",
        "IncompleteBody",
        "InvalidDigest",
        "XAmzContentChecksumMismatch",
        "XAmzContentSHA256Mismatch",
    };

    /// <summary>
    /// <inheritdoc cref="IFileContentStore.WriteAsync"/>
    /// One <c>PutObject</c> carrying length and SHA-256, so the store keeps the whole object or nothing.
    /// No total budget: a write lasts as long as its body.
    /// </summary>
    public async Task<ContentWriteOutcome> WriteAsync(
        string objectKey,
        Stream content,
        string declaredMediaType,
        long sizeInBytes,
        string sha256Hex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredMediaType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);
        string checksum = Base64DigestOf(sha256Hex);

        if (content.CanSeek && content.Length - content.Position != sizeInBytes)
        {
            return ContentWriteOutcome.ContentMismatch;
        }

        var request = new PutObjectRequest
        {
            BucketName = options.Value.BucketName,
            Key = objectKey,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = declaredMediaType,
            ChecksumSHA256 = checksum,
        };
        request.Headers.ContentLength = sizeInBytes;

        try
        {
            await client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.ErrorCode is { } code && _contentMismatchCodes.Contains(code))
        {
            return ContentWriteOutcome.ContentMismatch;
        }

        return ContentWriteOutcome.Written;
    }

    public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        // The budget bounds opening the object; reading it is bounded by whoever reads.
        using var budget = BucketBudget.Start(cancellationToken);

        try
        {
            var response = await client.GetObjectAsync(
                new GetObjectRequest { BucketName = options.Value.BucketName, Key = objectKey },
                budget.Token);

            return response.ResponseStream;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// <inheritdoc cref="IFileContentStore.DescribeAsync"/>
    /// The digest is the one the store recorded at write time; an object without one is a fault, not a
    /// reason to answer another digest that would fail every confirmation.
    /// </summary>
    public async Task<StoredObjectDescription?> DescribeAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        using var budget = BucketBudget.Start(cancellationToken);

        GetObjectMetadataResponse metadata;

        try
        {
            metadata = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = options.Value.BucketName,
                    Key = objectKey,

                    // The store omits the checksum header unless the caller asks for it.
                    ChecksumMode = ChecksumMode.ENABLED,
                },
                budget.Token);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return new StoredObjectDescription(metadata.ContentLength, HexChecksumOf(metadata, objectKey));
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        using var budget = BucketBudget.Start(cancellationToken);

        try
        {
            await client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = options.Value.BucketName, Key = objectKey },
                budget.Token);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // S3 answers 204 for a missing key; a compatible store answering 404 is the same answer.
        }
    }

    /// <summary>The digest as S3 wants it: base64 of the raw bytes.</summary>
    internal static string Base64DigestOf(string sha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);

        if (sha256Hex.Length != _sha256DigestLength * 2)
        {
            throw new ArgumentException(
                $"A SHA-256 is {_sha256DigestLength * 2} hexadecimal characters; this one is {sha256Hex.Length}.",
                nameof(sha256Hex));
        }

        return Convert.ToBase64String(Convert.FromHexString(sha256Hex));
    }

    /// <summary>The recorded digest as lower-case hex; the store reports base64 of the same bytes.</summary>
    private static string HexChecksumOf(GetObjectMetadataResponse metadata, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(metadata.ChecksumSHA256))
        {
            throw new InvalidOperationException(
                $"The object stored under '{objectKey}' carries no SHA-256 checksum. Every grant and " +
                "every write this module makes asks the store to record one, so the object was " +
                "stored some other way or the store ignored it.");
        }

        byte[] digest = Convert.FromBase64String(metadata.ChecksumSHA256);

        if (digest.Length != _sha256DigestLength)
        {
            throw new InvalidOperationException(
                $"The object stored under '{objectKey}' reports a SHA-256 of {digest.Length} bytes, " +
                $"and a SHA-256 is {_sha256DigestLength}.");
        }

        return Convert.ToHexStringLower(digest);
    }
}
