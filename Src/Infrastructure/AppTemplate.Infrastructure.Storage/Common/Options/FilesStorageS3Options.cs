using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Common.Options;

/// <summary>
/// The S3-compatible bucket the Files bytes live in, bound from <c>Files:Storage:S3</c> and validated
/// only when <c>Files:Storage:Provider</c> is <c>S3</c>. The bucket name is a setting, never a
/// constant, so a staging process cannot write into the production bucket.
/// </summary>
public sealed class FilesStorageS3Options
{
    public const string SectionName = "Files:Storage:S3";

    /// <summary>
    /// The longest lifetime Signature Version 4 allows a presigned URL; stated here so the failure is
    /// not a signing exception on someone's first upload.
    /// </summary>
    public static readonly TimeSpan MaxSignableLifetime = TimeSpan.FromDays(7);

    /// <summary>The bucket every object of this application is written to and read from.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// The region the signature is computed for. MinIO validates the signature against it, so a
    /// mismatch shows as a rejected signature.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// The S3-compatible endpoint <b>this process</b> talks to (metadata, deletes, listings, inspection
    /// reads, writes), empty for AWS. Clients use <see cref="PublicEndpoint"/>.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The endpoint <b>signed URLs are minted for</b>, empty to sign for
    /// <see cref="Endpoint"/>. A Signature Version 4 URL covers its host, so it must be signed for
    /// the name the client will use — set this whenever the store has an internal and an external
    /// name (MinIO on a Docker network). Its scheme decides the scheme of every signed URL minted.</summary>
    public string PublicEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// <see cref="PublicEndpoint"/> when set, otherwise <see cref="Endpoint"/>; the one answer the
    /// client factory and the validator share.
    /// </summary>
    public string SigningEndpoint =>
        string.IsNullOrWhiteSpace(PublicEndpoint) ? Endpoint : PublicEndpoint;

    /// <summary>Addresses the bucket as <c>host/bucket/key</c>; required behind container hostnames.</summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// Empty in a deployment with an instance role, whose credentials the SDK chain supplies. Set both
    /// keys only for a store with no such chain.
    /// </summary>
    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Allows a plain-HTTP <see cref="SigningEndpoint"/> on a non-loopback host, for
    /// containerised development stores such as MinIO. It governs only the endpoint URLs are minted
    /// for: a signed URL is a reusable bearer right that would travel in clear to the client, whereas
    /// the server-to-store <see cref="Endpoint"/> carries per-request signatures only.</summary>
    public bool AllowInsecureTransport { get; set; }

    /// <summary>
    /// The operator's ceiling on any grant lifetime. The default is the longest window a use case asks
    /// for (30 minutes to deposit a large file); anything longer is a write right left in logs.
    /// </summary>
    public TimeSpan MaxGrantLifetime { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Refuses what a client would otherwise discover: a malformed bucket name, half a credential pair, a
/// plaintext signing endpoint.
/// </summary>
internal sealed class FilesStorageS3OptionsValidator : IValidateOptions<FilesStorageS3Options>
{
    /// <summary>S3 bucket-name length bounds; with the alphabet, the only rules checkable offline.</summary>
    private const int _minBucketNameLength = 3;

    private const int _maxBucketNameLength = 63;

    public ValidateOptionsResult Validate(string? name, FilesStorageS3Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateBucketName(options, failures);

        if (string.IsNullOrWhiteSpace(options.Region))
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:Region' is required. It is part of the signing key, " +
                "so there is no neutral value to fall back to.");
        }

        ValidateEndpoints(options, failures);
        ValidateCredentials(options, failures);
        ValidateGrantLifetime(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateBucketName(FilesStorageS3Options options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            failures.Add($"'{FilesStorageS3Options.SectionName}:BucketName' is required.");

            return;
        }

        string bucket = options.BucketName;

        if (bucket.Length is < _minBucketNameLength or > _maxBucketNameLength)
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:BucketName' must be between {_minBucketNameLength} " +
                $"and {_maxBucketNameLength} characters.");
        }

        // Upper case is the one worth naming: it fails with a signature or redirect that never says why.
        if (!bucket.All(IsBucketNameCharacter))
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:BucketName' may only contain lower-case letters, " +
                "digits, '-' and '.'.");
        }
        else if (!char.IsAsciiLetterLower(bucket[0]) && !char.IsAsciiDigit(bucket[0])
            || !char.IsAsciiLetterLower(bucket[^1]) && !char.IsAsciiDigit(bucket[^1]))
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:BucketName' must start and end with a lower-case " +
                "letter or a digit.");
        }
    }

    /// <summary>
    /// Both endpoints must be well-formed http(s) URLs; the transport rule applies to
    /// <see cref="FilesStorageS3Options.SigningEndpoint"/> alone, because only URLs minted for it are
    /// bearer rights (see <see cref="FilesStorageS3Options.AllowInsecureTransport"/>).
    /// </summary>
    private static void ValidateEndpoints(FilesStorageS3Options options, List<string> failures)
    {
        ValidateEndpointSyntax(options.Endpoint, nameof(FilesStorageS3Options.Endpoint), failures);
        ValidateEndpointSyntax(options.PublicEndpoint, nameof(FilesStorageS3Options.PublicEndpoint), failures);
        ValidateSigningTransport(options, failures);
    }

    private static void ValidateEndpointSyntax(string endpoint, string key, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            // Absent means AWS S3 itself for Endpoint, and 'sign for Endpoint' for PublicEndpoint.
            return;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:{key}' must be an absolute http or https URL, " +
                "for example 'http://minio:9000'.");
        }
    }

    private static void ValidateSigningTransport(FilesStorageS3Options options, List<string> failures)
    {
        if (!Uri.TryCreate(options.SigningEndpoint, UriKind.Absolute, out var signing)
            || signing.Scheme != Uri.UriSchemeHttp
            || signing.IsLoopback
            || options.AllowInsecureTransport)
        {
            return;
        }

        // Named for the key that carries the value: a deployment that never wrote PublicEndpoint
        // would otherwise be sent looking for a line that is not in its file.
        string key = string.IsNullOrWhiteSpace(options.PublicEndpoint)
            ? nameof(FilesStorageS3Options.Endpoint)
            : nameof(FilesStorageS3Options.PublicEndpoint);

        failures.Add(
            $"'{FilesStorageS3Options.SectionName}:{key}' is an http URL against a host that is not " +
            "loopback, so every signed URL minted from it — each of which is a bearer right to " +
            $"read or write one object — travels in clear text. Use https, or set " +
            $"'{FilesStorageS3Options.SectionName}:AllowInsecureTransport' to true to accept that " +
            "deliberately.");
    }

    private static void ValidateCredentials(FilesStorageS3Options options, List<string> failures)
    {
        bool hasKeyId = !string.IsNullOrWhiteSpace(options.AccessKeyId);
        bool hasSecret = !string.IsNullOrWhiteSpace(options.SecretAccessKey);

        // Neither is the supported case: the SDK's credential chain supplies an instance role's
        // credentials. One of the two is a process that starts and signs anonymously.
        if (hasKeyId != hasSecret)
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:AccessKeyId' and " +
                $"'{FilesStorageS3Options.SectionName}:SecretAccessKey' are set together or not at all. " +
                "Leaving both empty is how a deployment with an instance role is configured.");
        }
    }

    private static void ValidateGrantLifetime(FilesStorageS3Options options, List<string> failures)
    {
        if (options.MaxGrantLifetime <= TimeSpan.Zero)
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:MaxGrantLifetime' must be positive; a ceiling of " +
                "zero would refuse every grant the application asks for.");
        }
        else if (options.MaxGrantLifetime > FilesStorageS3Options.MaxSignableLifetime)
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:MaxGrantLifetime' cannot exceed " +
                $"{FilesStorageS3Options.MaxSignableLifetime}, which is the longest lifetime a Signature " +
                "Version 4 presigned URL can carry.");
        }
    }

    private static bool IsBucketNameCharacter(char character) =>
        char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.';
}
