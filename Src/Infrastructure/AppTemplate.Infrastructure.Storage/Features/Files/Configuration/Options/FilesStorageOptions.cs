using AppTemplate.Application.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;

public sealed class FilesStorageOptions
{
    public const string SectionName = "Files:Storage";

    public const string S3Provider = "S3";

    public const string FileSystemProvider = "FileSystem";

    public string Provider { get; set; } = S3Provider;

    internal bool UsesFileSystem => string.Equals(Provider, FileSystemProvider, StringComparison.OrdinalIgnoreCase);
}

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
