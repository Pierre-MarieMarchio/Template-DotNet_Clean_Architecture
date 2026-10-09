using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Transfers;

/// <summary>
/// <see cref="IFileDirectTransfer"/> over an S3-compatible bucket: Signature Version 4 presigned
/// URLs, so the bytes travel between the client and the store.
/// <para>
/// <paramref name="signer"/> is built on <see cref="FilesStorageS3Options.SigningEndpoint"/>: a presigned
/// URL covers the host it was signed for, and nothing downstream can correct it. Presigning is a
/// local hash and opens no connection.
/// </para>
/// </summary>
internal sealed class S3FileDirectTransfer(
    [FromKeyedServices(BucketClientFactory.SigningClientKey)] IAmazonS3 signer,
    IOptions<FilesStorageS3Options> options) : IFileDirectTransfer
{
    /// <summary>
    /// Carries the digest itself, base64-encoded and signed: the store refuses other bytes and records
    /// it, which is what lets <c>DescribeAsync</c> answer with a digest at all.
    /// </summary>
    private const string _checksumHeader = "x-amz-checksum-sha256";

    private const string _uploadMethod = "PUT";

    public bool IsSupported => true;

    public async Task<IssuedUploadGrant> CreateUploadGrantAsync(
        string objectKey,
        string declaredMediaType,
        long sizeInBytes,
        string declaredSha256,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredMediaType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredSha256);
        cancellationToken.ThrowIfCancellationRequested();

        var expiresAt = ExpiryFor(lifetime);
        var request = PreSignedRequest(objectKey, HttpVerb.PUT, expiresAt);

        // Every header set here is signed; a deposit sending anything else is refused by the store.
        request.Headers.ContentType = declaredMediaType;
        request.Headers.ContentLength = sizeInBytes;
        request.Headers[_checksumHeader] = S3FileContentStore.Base64DigestOf(declaredSha256);

        string url = await signer.GetPreSignedURLAsync(request);

        return new IssuedUploadGrant(url, _uploadMethod, RequiredHeadersOf(request), expiresAt);
    }

    public async Task<IssuedDownloadGrant> CreateDownloadGrantAsync(
        string objectKey,
        string downloadFileName,
        string declaredMediaType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredMediaType);
        cancellationToken.ThrowIfCancellationRequested();

        var expiresAt = ExpiryFor(lifetime);
        var request = PreSignedRequest(objectKey, HttpVerb.GET, expiresAt);

        // Signed query parameters, so the client cannot turn the attachment into something rendered.
        request.ResponseHeaderOverrides.ContentType = declaredMediaType;
        request.ResponseHeaderOverrides.ContentDisposition = DownloadDispositionPolicy.AttachmentFor(downloadFileName);

        string url = await signer.GetPreSignedURLAsync(request);

        return new IssuedDownloadGrant(url, expiresAt);
    }

    /// <summary>
    /// The lifetime actually signed, clamped to <see cref="FilesStorageS3Options.MaxGrantLifetime"/>. The
    /// store's deadline may fall up to one second earlier: SigV4 floors its date to the second.
    /// </summary>
    private DateTimeOffset ExpiryFor(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var granted = lifetime <= options.Value.MaxGrantLifetime ? lifetime : options.Value.MaxGrantLifetime;

        // The system clock, not the injectable one: the SDK signs from the machine's clock.
        return DateTimeOffset.UtcNow.Add(granted);
    }

    private GetPreSignedUrlRequest PreSignedRequest(string objectKey, HttpVerb verb, DateTimeOffset expiresAt) =>
        new()
        {
            BucketName = options.Value.BucketName,
            Key = objectKey,
            Verb = verb,

            // The SDK defaults to HTTPS whatever the endpoint says; the signing endpoint's scheme wins.
            Protocol = InsecureEndpoint() ? Protocol.HTTP : Protocol.HTTPS,
            Expires = expiresAt.UtcDateTime,
        };

    private bool InsecureEndpoint() =>
        options.Value.SigningEndpoint.StartsWith(Uri.UriSchemeHttp + "://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Read off the signed request rather than listed twice, so the grant cannot name headers the
    /// signature does not cover.
    /// </summary>
    private static Dictionary<string, string> RequiredHeadersOf(GetPreSignedUrlRequest request) =>
        request.Headers.Keys.ToDictionary(
            header => header,
            header => request.Headers[header],
            StringComparer.OrdinalIgnoreCase);
}
