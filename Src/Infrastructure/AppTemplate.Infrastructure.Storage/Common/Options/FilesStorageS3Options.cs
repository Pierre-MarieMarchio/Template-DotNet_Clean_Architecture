using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Common.Options;

/// <summary>
/// The S3-compatible bucket the Files feature's bytes live in, when <c>Files:Storage:Provider</c> is
/// <c>S3</c>. Bound from <c>Files:Storage:S3</c> and validated at start-up only for that provider.
/// <para>
/// The bucket name is a setting, never a constant: a compiled-in name would let a staging process
/// write into the production bucket.
/// </para>
/// </summary>
public sealed class FilesStorageS3Options
{
    public const string SectionName = "Files:Storage:S3";

    /// <summary>
    /// AWS Signature Version 4 refuses to sign a presigned URL valid for more than seven days, so no
    /// configured ceiling above this could ever be honoured. It is stated here rather than left to
    /// the SDK because the failure would otherwise arrive as a signing exception on the first upload
    /// of whoever set it.
    /// </summary>
    public static readonly TimeSpan MaxSignableLifetime = TimeSpan.FromDays(7);

    /// <summary>The bucket every object of this application is written to and read from.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// The region the signature is computed for. It is part of the signing key, so it must match
    /// what the store expects even when the store is not AWS: MinIO accepts any value and validates
    /// the signature against the one it was given, which means a mismatch presents as a rejected
    /// signature rather than as a wrong region.
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// The S3-compatible endpoint <b>this process</b> talks to, empty for AWS S3 itself. Set for
    /// MinIO in development and for any other compatible store. What travels over it is what this
    /// process does itself — metadata reads, deletes, listings, the reads of the inspection pass and
    /// writes through the store; a direct deposit or download is the client's own request, made
    /// against <see cref="PublicEndpoint"/>.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// The endpoint <b>signed URLs are minted for</b>, empty to sign for <see cref="Endpoint"/>.
    /// <para>
    /// It exists because a Signature Version 4 presigned URL covers the host it was signed for —
    /// <c>host</c> is always in <c>X-Amz-SignedHeaders</c> — so a URL signed for one name and
    /// followed under another is refused with <c>SignatureDoesNotMatch</c>, even when both names
    /// reach the same server on the same port. No proxy, no API and no client can rewrite the host
    /// afterwards; the name has to be right at signing time.
    /// </para>
    /// <para>
    /// Every self-hosted store therefore has two names and this has to be set: MinIO on a Docker
    /// network is <c>minio:9000</c> to the API and <c>localhost:9000</c> to the browser, and a
    /// cluster-internal service name is not resolvable outside its cluster. AWS S3 is the one case
    /// where it stays empty along with <see cref="Endpoint"/>, because there is only ever one name.
    /// </para>
    /// <para>
    /// <b>Its scheme decides the scheme of every signed URL this module mints</b>, and it is the
    /// endpoint <see cref="AllowInsecureTransport"/> governs — see that property for why the rule is
    /// here rather than on <see cref="Endpoint"/>.
    /// </para>
    /// </summary>
    public string PublicEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// The endpoint a signature is actually computed against: <see cref="PublicEndpoint"/> when a
    /// deployment states one, and <see cref="Endpoint"/> when it does not. Derived rather than
    /// bound, so there is one answer to the question and both the client factory and the validator
    /// read it.
    /// </summary>
    public string SigningEndpoint =>
        string.IsNullOrWhiteSpace(PublicEndpoint) ? Endpoint : PublicEndpoint;

    /// <summary>
    /// Addresses the bucket as a path segment (<c>host/bucket/key</c>) instead of as a subdomain
    /// (<c>bucket.host/key</c>). Required by every compatible store reached through a container
    /// hostname, where the subdomain form resolves to nothing.
    /// </summary>
    public bool ForcePathStyle { get; set; }

    /// <summary>
    /// Left empty in a deployment that has an instance role: the SDK's own credential chain then
    /// supplies short-lived credentials nobody has to rotate by hand. Filled in only where there is
    /// no such chain — a development MinIO, a compatible store outside AWS.
    /// </summary>
    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Explicit opt-in to an unencrypted <see cref="SigningEndpoint"/> whose host is not loopback.
    /// Exists for a containerised development store such as MinIO, which speaks plain HTTP under a
    /// hostname that is not <c>localhost</c>. The same shape as
    /// <c>EmailOptions.AllowInsecureTransport</c> and for a sharper reason: a signed URL is a bearer
    /// credential, and one minted for an <c>http://</c> endpoint is handed to a client that will
    /// send it in clear text.
    /// <para>
    /// <b>It governs the endpoint URLs are minted for, and nothing else.</b> A plain-HTTP
    /// <see cref="Endpoint"/> is server-to-server traffic inside a network the operator chose — a
    /// service mesh, a Docker network, a VPC — and Signature Version 4 puts a per-request signature
    /// on the wire rather than the secret key, so watching it yields nothing anyone can re-use. It is
    /// not free of content: the inspection pass reads objects over it.
    /// A plain-HTTP <see cref="PublicEndpoint"/> is a different thing all the same: every grant this
    /// module mints is a <em>reusable</em> right to read or write one object, valid until it expires,
    /// travelling in clear to a client this deployment does not control and into whatever proxy log,
    /// referrer header and browser history it passes through. Only the second is worth stopping a
    /// process over, so only the second asks permission.
    /// </para>
    /// </summary>
    public bool AllowInsecureTransport { get; set; }

    /// <summary>
    /// The longest a minted grant may be valid for, whatever lifetime a caller asks for. A ceiling
    /// rather than the lifetime itself: how long an upload window or a download window should be is
    /// a decision the use cases make and state, and this is the operator's cap over all of them.
    /// <para>
    /// The default is the longest window any use case in this repository asks for — the 30 minutes
    /// <c>RegisterFileUseCase</c> gives a client to deposit five gigabytes — so it is a value derived
    /// from what the feature does rather than a round number picked for looking safe. Anything longer
    /// than the deposit it was minted for is a write right sitting in somebody's logs.
    /// </para>
    /// </summary>
    public TimeSpan MaxGrantLifetime { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// What a storage configuration is allowed to be. Every rule here is one whose violation would
/// otherwise be discovered by a client: a malformed bucket name is a rejected request, a missing
/// half of a credential pair is an unsigned call, and a plaintext signing endpoint is a credential
/// in the clear that nothing reports at all.
/// </summary>
internal sealed class FilesStorageS3OptionsValidator : IValidateOptions<FilesStorageS3Options>
{
    /// <summary>
    /// S3's own bucket naming rules, in the only two respects that can be checked without a network
    /// call: length, and the alphabet. A name outside them is refused by every S3-compatible store,
    /// so accepting it here only moves the failure to the first request.
    /// </summary>
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

        // Upper case is the one worth naming: a bucket created as `AppFiles` exists under that name
        // nowhere, and the request fails with a signature or a redirect that never mentions casing.
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
    /// Both endpoints must be addresses; only the one URLs are signed for must be encrypted.
    /// <para>
    /// <b>Which rule lands on which endpoint is the whole point of the split.</b> Being a well-formed
    /// http or https URL is a property each key needs on its own — a malformed
    /// <see cref="FilesStorageS3Options.Endpoint"/> breaks the three server-to-store calls, a malformed
    /// <see cref="FilesStorageS3Options.PublicEndpoint"/> breaks every grant — so each is refused under its
    /// own name. The transport rule lands on <see cref="FilesStorageS3Options.SigningEndpoint"/> alone,
    /// because what it protects is the bearer right a signed URL is, and that right exists only on
    /// the endpoint URLs are minted for. See <see cref="FilesStorageS3Options.AllowInsecureTransport"/>.
    /// </para>
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
