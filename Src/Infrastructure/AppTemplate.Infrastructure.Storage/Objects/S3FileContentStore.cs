using System.Globalization;
using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Buckets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Objects;

/// <summary>
/// <see cref="IFileContentStore"/> over an S3-compatible object store.
/// <para>
/// <b>The prefix is what tells this adapter from the double.</b> Two modules implement this port —
/// this one and <c>InMemoryFileContentStore</c> — so each names the technology it is, exactly as
/// <c>MailKitEmailSender</c> and <c>InMemoryEmailSender</c> do.
/// </para>
/// <para>
/// <b>Not one byte of any file passes through this class</b>, and every method below is shaped by
/// that. A deposit is a signed <c>PUT</c> the client makes itself; a read is a signed <c>GET</c> the
/// client follows itself; confirmation asks the store what it holds instead of reading it. The port
/// promises this and it is cheap to keep here — but see <see cref="DescribeAsync"/> for the one
/// place where keeping it costs something.
/// </para>
/// <para>
/// <b>This module escapes the hosts' outbound HTTP policy</b>, because the AWS SDK builds its own
/// <c>HttpClient</c> and never meets <c>IHttpClientFactory</c>. <see cref="BucketBudget"/> is where
/// the same budget is restated for it, and why.
/// </para>
/// <para>
/// <b>Two clients, and they are not interchangeable.</b> <paramref name="client"/> is the one this
/// process calls the store with; <paramref name="signer"/> mints URLs a client outside this process
/// will follow, and is built on <see cref="StorageOptions.SigningEndpoint"/> because a Signature
/// Version 4 URL covers the host it was signed for and nothing downstream can correct it. Using
/// either where the other belongs produces a working call and a broken URL, or the reverse, and both
/// are silent here.
/// </para>
/// </summary>
internal sealed class S3FileContentStore(
    IAmazonS3 client,
    [FromKeyedServices(BucketClientFactory.SigningClientKey)] IAmazonS3 signer,
    IOptions<StorageOptions> options) : IFileContentStore
{
    /// <summary>
    /// Asks the store to record a SHA-256 of the deposited bytes. It is part of the signature, so a
    /// deposit cannot drop it, and it is what makes <see cref="DescribeAsync"/> able to answer with
    /// a digest of the object rather than with an entity tag that is not one.
    /// </summary>
    private const string _checksumAlgorithmHeader = "x-amz-sdk-checksum-algorithm";

    private const string _checksumAlgorithm = "SHA256";

    /// <summary>The method the upload grant is signed for. A signature covers one verb.</summary>
    private const string _uploadMethod = "PUT";

    private const int _sha256DigestLength = 32;

    public async Task<IssuedUploadGrant> CreateUploadGrantAsync(
        string objectKey,
        string declaredMediaType,
        long sizeInBytes,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredMediaType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var expiresAt = ExpiryFor(lifetime);
        var request = PreSignedRequest(objectKey, HttpVerb.PUT, expiresAt);

        // Every header set here is covered by the signature, which is the whole mechanism: the store
        // recomputes the signature from what the deposit actually sends, so a client that declared
        // one media type or one size and deposits another is refused by the store rather than at
        // confirmation, with nothing written.
        request.Headers.ContentType = declaredMediaType;
        request.Headers.ContentLength = sizeInBytes;
        request.Headers[_checksumAlgorithmHeader] = _checksumAlgorithm;

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

        // Overrides travel as signed query parameters, so the client cannot change what the store
        // will answer with. The disposition is an attachment rather than inline: the declared media
        // type is the client's own claim about bytes nothing here has read, and a store that offered
        // to render it would be letting an uploader choose what a viewer's browser executes.
        request.ResponseHeaderOverrides.ContentType = declaredMediaType;
        request.ResponseHeaderOverrides.ContentDisposition = AttachmentDispositionFor(downloadFileName);

        string url = await signer.GetPreSignedURLAsync(request);

        return new IssuedDownloadGrant(url, expiresAt);
    }

    /// <summary>
    /// <inheritdoc cref="IFileContentStore.DescribeAsync"/>
    /// <para>
    /// <b>The digest is the store's, computed at deposit time, and this adapter never falls back to
    /// computing one itself.</b> The port allows for an adapter that owes the computation where its
    /// store cannot record a SHA-256; an S3-compatible store can, this adapter asks it to on every
    /// upload grant it mints, and the alternative would mean streaming up to five gigabytes through
    /// a process whose whole design is that no byte of a file passes through it. So an object
    /// carrying no SHA-256 is reported as a fault rather than papered over: it was deposited outside
    /// a grant this adapter minted, or against a store that ignored the header, and either way
    /// answering with some other digest would fail every confirmation with a mismatch that names
    /// nothing.
    /// </para>
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

                    // Without this the store returns the checksum it stored for nobody: the header
                    // is omitted from the response unless the caller asks for it.
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
            // S3 itself answers 204 for a key that is not there, which is what the port requires.
            // A compatible store that answers 404 instead must not turn the fast path and the sweep
            // reaching the same key into an error, since neither is coordinated with the other.
        }
    }

    /// <summary>
    /// The lifetime actually signed, clamped to the operator's ceiling. Clamping rather than
    /// refusing, and the instant returned in the grant is computed from the clamped value, so the
    /// caller is told how long the URL it is holding will work for and the correction is always
    /// shorter.
    /// <para>
    /// <b>To within a second, and in the unsafe direction.</b> Signature Version 4 carries
    /// <c>X-Amz-Date</c> floored to the whole second and <c>X-Amz-Expires</c> as a whole number of
    /// seconds, so the deadline the store computes can fall up to one second before the instant this
    /// returns. Measured against MinIO, not reasoned about: a grant asked for with a one-second
    /// lifetime is refused on its first request while its own <c>ExpiresAt</c> is still in the
    /// future. It is immaterial at the five-minute download window the API actually issues and it is
    /// worth knowing before anyone mints a grant measured in seconds — a caller that treats
    /// <c>ExpiresAt</c> as the last usable instant is right by minutes and wrong by a second.
    /// </para>
    /// </summary>
    private DateTimeOffset ExpiryFor(TimeSpan lifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);

        var granted = lifetime <= options.Value.MaxGrantLifetime ? lifetime : options.Value.MaxGrantLifetime;

        // The system clock, deliberately, and not the injectable one. The signature's own validity
        // window is computed by the SDK from the machine's clock; an expiry taken from a clock a
        // test can move would disagree with the signature the same call produced, and the grant
        // would announce a deadline the store does not honour.
        return DateTimeOffset.UtcNow.Add(granted);
    }

    private GetPreSignedUrlRequest PreSignedRequest(string objectKey, HttpVerb verb, DateTimeOffset expiresAt) =>
        new()
        {
            BucketName = options.Value.BucketName,
            Key = objectKey,
            Verb = verb,

            // The SDK defaults this to HTTPS whatever the endpoint says, so a store reached over
            // plain HTTP would be handed URLs whose scheme it does not answer on. Whether an
            // unencrypted signing endpoint is acceptable at all is decided once, by
            // StorageOptionsValidator; here it only has to be described accurately.
            Protocol = InsecureEndpoint() ? Protocol.HTTP : Protocol.HTTPS,
            Expires = expiresAt.UtcDateTime,
        };

    /// <summary>
    /// Read off the endpoint the URL is signed for, not the one this process talks to. The two
    /// differ in the deployment this exists for — plain HTTP inside a mesh, HTTPS at the ingress —
    /// and a scheme taken from the internal endpoint would hand every client an address the public
    /// name does not answer on, with a signature that forbids correcting it.
    /// </summary>
    private bool InsecureEndpoint() =>
        options.Value.SigningEndpoint.StartsWith(Uri.UriSchemeHttp + "://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The headers the deposit must send back verbatim, read off the request that was signed rather
    /// than listed a second time. Listing them twice is how the two come to disagree, and the shape
    /// of that defect is every upload failing with a signature error while the grant says which
    /// headers it covers.
    /// </summary>
    private static Dictionary<string, string> RequiredHeadersOf(GetPreSignedUrlRequest request) =>
        request.Headers.Keys.ToDictionary(
            header => header,
            header => request.Headers[header],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The digest the store recorded, as the lower-case hexadecimal the port asks for. The store
    /// reports base64, which is the same 32 bytes written differently — comparing the two encodings
    /// as strings would fail every confirmation.
    /// </summary>
    private static string HexChecksumOf(GetObjectMetadataResponse metadata, string objectKey)
    {
        if (string.IsNullOrWhiteSpace(metadata.ChecksumSHA256))
        {
            throw new InvalidOperationException(
                $"The object stored under '{objectKey}' carries no SHA-256 checksum. Every upload " +
                "grant this adapter mints signs the header that asks the store to record one, so " +
                "the object was deposited some other way or the store ignored it.");
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

    /// <summary>
    /// The <c>Content-Disposition</c> the download will carry. The name is a label the user chose,
    /// so it is written twice, exactly as RFC 6266 provides for: a quoted ASCII form every client
    /// understands, and a <c>filename*</c> that carries the real name in UTF-8. Characters that
    /// would end the quoted string early are replaced rather than escaped, because the fallback only
    /// has to be safe and recognisable — the parameter beside it is the accurate one.
    /// </summary>
    private static string AttachmentDispositionFor(string downloadFileName)
    {
        var fallback = new StringBuilder(downloadFileName.Length);

        foreach (char character in downloadFileName)
        {
            fallback.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ' '
                ? character
                : '_');
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"attachment; filename=\"{fallback}\"; filename*=UTF-8''{Uri.EscapeDataString(downloadFileName)}");
    }
}
