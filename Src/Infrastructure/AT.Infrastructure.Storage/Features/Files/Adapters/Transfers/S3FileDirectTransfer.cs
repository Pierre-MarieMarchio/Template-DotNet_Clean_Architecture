using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Transfers;

internal sealed class S3FileDirectTransfer(
    [FromKeyedServices(BucketClientFactory.SigningClientKey)] IAmazonS3 signer,
    IOptions<FilesStorageS3Options> options) : IFileDirectTransfer
{
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

        request.ResponseHeaderOverrides.ContentType = declaredMediaType;
        request.ResponseHeaderOverrides.ContentDisposition = DownloadDispositionPolicy.AttachmentFor(downloadFileName);

        string url = await signer.GetPreSignedURLAsync(request);

        return new IssuedDownloadGrant(url, expiresAt);
    }

    private DateTimeOffset ExpiryFor(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var granted = lifetime <= options.Value.MaxGrantLifetime ? lifetime : options.Value.MaxGrantLifetime;

        return DateTimeOffset.UtcNow.Add(granted);
    }

    private GetPreSignedUrlRequest PreSignedRequest(string objectKey, HttpVerb verb, DateTimeOffset expiresAt) =>
        new()
        {
            BucketName = options.Value.BucketName,
            Key = objectKey,
            Verb = verb,

            Protocol = InsecureEndpoint() ? Protocol.HTTP : Protocol.HTTPS,
            Expires = expiresAt.UtcDateTime,
        };

    private bool InsecureEndpoint() =>
        options.Value.SigningEndpoint.StartsWith(Uri.UriSchemeHttp + "://", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> RequiredHeadersOf(GetPreSignedUrlRequest request) =>
        request.Headers.Keys.ToDictionary(
            header => header,
            header => request.Headers[header],
            StringComparer.OrdinalIgnoreCase);
}
