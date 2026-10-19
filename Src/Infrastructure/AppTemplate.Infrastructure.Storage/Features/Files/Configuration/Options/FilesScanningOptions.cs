using AppTemplate.Application.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;

/// <summary>
/// Whether deposited files are scanned by a <c>clamd</c> daemon and how it is reached; bound from
/// <c>Files:Scanning</c>. Disabled in the shipped state: the signature check still runs, and the
/// missing antivirus verdict is recorded in <c>SECURITY.md</c>.
/// </summary>
public sealed class FilesScanningOptions
{
    public const string SectionName = "Files:Scanning";

    /// <summary>The default port <c>clamd</c> listens on.</summary>
    public const int DefaultPort = 3310;

    /// <summary>Whether files are sent to the scanner. <c>false</c> registers a scanner that finds nothing.</summary>
    public bool Enabled { get; set; }

    /// <summary>The host running <c>clamd</c>; a host and port, not a URL: it speaks raw TCP.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// The largest content offered to the scanner, at or below the daemon's <c>StreamMaxLength</c>.
    /// Larger content is refused as not inspectable, so raising <c>FileSize.MaxBytes</c> means raising both.
    /// </summary>
    public long MaxScannableBytes { get; set; } = 25L * 1024 * 1024;
}

/// <summary>Refuses values whose only symptom would be a user's: no host, or a ceiling refusing every file.</summary>
internal sealed class FilesScanningOptionsValidator : IValidateOptions<FilesScanningOptions>
{
    /// <summary>Below a megabyte, ordinary files would be refused as not inspectable.</summary>
    private const long _minimumScannableBytes = 1024L * 1024;

    public ValidateOptionsResult Validate(string? name, FilesScanningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.Enabled && string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add(
                $"'{FilesScanningOptions.SectionName}:Host' is required when " +
                $"'{FilesScanningOptions.SectionName}:Enabled' is true.");
        }

        // Only when scanning: a disabled deployment leaves the port at a default nothing uses.
        if (options.Enabled && options.Port is < 1 or > 65535)
        {
            failures.Add($"'{FilesScanningOptions.SectionName}:Port' must be between 1 and 65535.");
        }

        if (options.MaxScannableBytes < _minimumScannableBytes)
        {
            failures.Add(
                $"'{FilesScanningOptions.SectionName}:MaxScannableBytes' must be at least " +
                $"{_minimumScannableBytes} bytes. A ceiling below that refuses ordinary files as " +
                "not inspectable, which quarantines them.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>Refuses a category that asks for a scan while scanning is disabled: it would never be scanned.</summary>
internal sealed class FilesCategoriesScanningValidator(IOptions<FilesScanningOptions> scanning)
    : IValidateOptions<FilesOptions>
{
    public ValidateOptionsResult Validate(string? name, FilesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (scanning.Value.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = options.Categories
            .Where(category => category.Value is { Scan: true })
            .Select(category =>
                $"'{FilesOptions.SectionName}:Categories:{category.Key}:Scan' is true while " +
                $"'{FilesScanningOptions.SectionName}:Enabled' is false.")
            .ToList();

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
