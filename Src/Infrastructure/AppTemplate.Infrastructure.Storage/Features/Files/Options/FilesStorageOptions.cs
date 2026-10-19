using AppTemplate.Application.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Options;

/// <summary>
/// Which store holds the bytes, bound from <c>Files:Storage</c>; only the chosen provider's own section
/// is bound and validated. Public because the section is part of the deployment contract.
/// </summary>
public sealed class FilesStorageOptions
{
    public const string SectionName = "Files:Storage";

    /// <summary>An S3-compatible bucket: signs URLs, so clients transfer bytes directly.</summary>
    public const string S3Provider = "S3";

    /// <summary>A directory on a local or mounted disk: cannot sign, so the API carries the bytes.</summary>
    public const string FileSystemProvider = "FileSystem";

    /// <summary><see cref="S3Provider"/> or <see cref="FileSystemProvider"/>, compared ignoring case.</summary>
    public string Provider { get; set; } = S3Provider;

    /// <summary>Whether <see cref="Provider"/> names the file system store.</summary>
    internal bool UsesFileSystem => string.Equals(Provider, FileSystemProvider, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Refuses a provider this module has no adapter for.</summary>
internal sealed class FilesStorageOptionsValidator : IValidateOptions<FilesStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, FilesStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UsesFileSystem
            || string.Equals(options.Provider, FilesStorageOptions.S3Provider, StringComparison.OrdinalIgnoreCase)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"'{FilesStorageOptions.SectionName}:Provider' must be '{FilesStorageOptions.S3Provider}' or " +
                $"'{FilesStorageOptions.FileSystemProvider}'.");
    }
}

/// <summary>
/// Refuses a category that can only be uploaded directly when the chosen store cannot sign URLs:
/// nothing could ever be uploaded into it.
/// </summary>
internal sealed class FilesCategoriesStorageValidator(IOptions<FilesStorageOptions> storage)
    : IValidateOptions<FilesOptions>
{
    public ValidateOptionsResult Validate(string? name, FilesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!storage.Value.UsesFileSystem)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = options.Categories
            .Where(category => category.Value is { } settings
                && settings.UploadModes.Count > 0
                && settings.UploadModes.All(mode =>
                    string.Equals(mode, FileCategoryOptions.DirectUploadMode, StringComparison.OrdinalIgnoreCase)))
            .Select(category =>
                $"'{FilesOptions.SectionName}:Categories:{category.Key}:UploadModes' allows only " +
                $"'{FileCategoryOptions.DirectUploadMode}', which the '{FilesStorageOptions.FileSystemProvider}' " +
                $"store cannot sign. Add '{FileCategoryOptions.ApiUploadMode}'.")
            .ToList();

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
