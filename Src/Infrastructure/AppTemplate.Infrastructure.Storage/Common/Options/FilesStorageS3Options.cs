using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Common.Options;

public sealed class FilesStorageS3Options
{
    public const string SectionName = "Files:Storage:S3";

    public static readonly TimeSpan MaxSignableLifetime = TimeSpan.FromDays(7);

    public string BucketName { get; set; } = string.Empty;

    public string Region { get; set; } = "us-east-1";

    public string Endpoint { get; set; } = string.Empty;

    public string PublicEndpoint { get; set; } = string.Empty;

    public string SigningEndpoint =>
        string.IsNullOrWhiteSpace(PublicEndpoint) ? Endpoint : PublicEndpoint;

    public bool ForcePathStyle { get; set; }

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public bool AllowInsecureTransport { get; set; }

    public TimeSpan MaxGrantLifetime { get; set; } = TimeSpan.FromMinutes(30);
}

internal sealed class FilesStorageS3OptionsValidator : IValidateOptions<FilesStorageS3Options>
{
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
            return;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(
                $"'{FilesStorageS3Options.SectionName}:{key}' must be an absolute http or https URL, " +
                "for example 'http://objectstore:9000'.");
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
