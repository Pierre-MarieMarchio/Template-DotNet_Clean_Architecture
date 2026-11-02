using AppTemplate.Application.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;

public sealed class FilesScanningOptions
{
    public const string SectionName = "Files:Scanning";

    public const int DefaultPort = 3310;

    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = DefaultPort;

    public long MaxScannableBytes { get; set; } = 25L * 1024 * 1024;
}

internal sealed class FilesScanningOptionsValidator : IValidateOptions<FilesScanningOptions>
{
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
