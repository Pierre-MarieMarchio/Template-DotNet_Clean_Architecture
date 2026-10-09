using System.Globalization;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IFileDirectTransfer"/> that mints real signatures over URLs that resolve nowhere —
/// see <see cref="StoredObjects"/>. A deposit against a grant is something a test performs, through
/// <see cref="StoredObjects.Deposit"/>.
/// </summary>
internal sealed class InMemoryFileDirectTransfer(StoredObjects objects, IDateTimeProvider dateTimeProvider)
    : IFileDirectTransfer
{
    private const string _uploadMethod = "PUT";

    private const string _downloadMethod = "GET";

    public bool IsSupported => true;

    public Task<IssuedUploadGrant> CreateUploadGrantAsync(
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
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var expiresAt = dateTimeProvider.UtcNow.Add(lifetime);

        // The headers a signed PUT against a real store covers, so a client written against the
        // double sends what the real adapter will require.
        var required = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = declaredMediaType,
            ["Content-Length"] = sizeInBytes.ToString(CultureInfo.InvariantCulture),
            ["x-amz-checksum-sha256"] = Convert.ToBase64String(Convert.FromHexString(declaredSha256)),
        };

        return Task.FromResult(new IssuedUploadGrant(
            objects.SignedUrl(_uploadMethod, objectKey, expiresAt),
            _uploadMethod,
            required,
            expiresAt));
    }

    public Task<IssuedDownloadGrant> CreateDownloadGrantAsync(
        string objectKey,
        string downloadFileName,
        string declaredMediaType,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredMediaType);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        var expiresAt = dateTimeProvider.UtcNow.Add(lifetime);

        return Task.FromResult(new IssuedDownloadGrant(
            objects.SignedUrl(_downloadMethod, objectKey, expiresAt, downloadFileName),
            expiresAt));
    }
}
