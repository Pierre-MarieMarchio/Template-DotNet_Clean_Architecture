using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Options;

/// <summary>
/// Which store holds the Files feature's bytes. Bound from <c>Files:Storage</c>; the chosen
/// provider's own section (<c>Files:Storage:S3</c> or <c>Files:Storage:FileSystem</c>) is the only
/// one bound and validated.
/// <para>Public because its section is part of the template's contract with whoever deploys it.</para>
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
