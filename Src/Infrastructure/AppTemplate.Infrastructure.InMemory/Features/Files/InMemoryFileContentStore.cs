using System.Globalization;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IFileContentStore"/> that files objects in memory and opens no socket.
/// <para>
/// Internal and sealed, like every other double here: the observable surface is
/// <see cref="StoredObjects"/>, not this class. A test that named this one would be asserting on the
/// double rather than on the behaviour it stands in for.
/// </para>
/// <para>
/// <b>It mints a real signature over a URL that resolves nowhere</b> — see
/// <see cref="StoredObjects"/> for why that is the honest arrangement. The alternative, pointing the
/// URL back at this process so that a development client could actually transfer bytes, would put
/// the API in the data path, which is the one thing the whole feature is designed to avoid; a double
/// that made it work would be teaching a shape that production does not have.
/// </para>
/// <para>
/// It does not throw, does not simulate a slow store and does not fail on demand: a double that
/// simulates failure modes accumulates a second implementation of the thing under test. A test
/// needing a failing store substitutes one for the single call it cares about.
/// </para>
/// </summary>
internal sealed class InMemoryFileContentStore(StoredObjects objects, IDateTimeProvider dateTimeProvider)
    : IFileContentStore
{
    private const string _uploadMethod = "PUT";

    private const string _downloadMethod = "GET";

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

        // The same three headers a signed PUT against a real store covers, so a client written
        // against the double sends what the real adapter will require of it — the digest included,
        // since that is what makes a grant authorise one body rather than any body of the right
        // length.
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

    public Task<StoredObjectDescription?> DescribeAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = objects.Find(objectKey);

        return Task.FromResult(stored is null
            ? null
            : new StoredObjectDescription(stored.SizeInBytes, stored.Checksum));
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        cancellationToken.ThrowIfCancellationRequested();

        // Silent on a key that is not there, as the port requires: the deletion path and the sweep
        // both reach it and neither knows about the other.
        objects.Remove(objectKey);

        return Task.CompletedTask;
    }
}
