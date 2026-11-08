using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;

public sealed class FilesStorageFileSystemOptions
{
    public const string SectionName = "Files:Storage:FileSystem";

    public string RootPath { get; set; } = string.Empty;
}

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
