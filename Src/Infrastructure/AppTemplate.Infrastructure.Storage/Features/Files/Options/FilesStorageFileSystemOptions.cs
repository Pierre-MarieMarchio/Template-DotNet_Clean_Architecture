using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Options;

/// <summary>
/// The directory the Files feature's bytes live in when <c>Files:Storage:Provider</c> is
/// <c>FileSystem</c>. Bound from <c>Files:Storage:FileSystem</c> and validated at start-up only for
/// that provider.
/// </summary>
public sealed class FilesStorageFileSystemOptions
{
    public const string SectionName = "Files:Storage:FileSystem";

    /// <summary>
    /// An absolute path, exclusive to this feature: the orphan sweep deletes every file under it that
    /// no row names. Created on first write when absent.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
}

/// <summary>
/// Refuses a missing or relative root. A relative one would resolve against the working directory,
/// which moves the store when the process is started from elsewhere.
/// </summary>
internal sealed class FilesStorageFileSystemOptionsValidator : IValidateOptions<FilesStorageFileSystemOptions>
{
    public ValidateOptionsResult Validate(string? name, FilesStorageFileSystemOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootPath))
        {
            return ValidateOptionsResult.Fail(
                $"'{FilesStorageFileSystemOptions.SectionName}:RootPath' is required when " +
                $"'{FilesStorageOptions.SectionName}:Provider' is '{FilesStorageOptions.FileSystemProvider}'.");
        }

        return Path.IsPathFullyQualified(options.RootPath)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"'{FilesStorageFileSystemOptions.SectionName}:RootPath' must be an absolute path.");
    }
}
