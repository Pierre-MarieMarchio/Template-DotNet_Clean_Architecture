using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.Configuration.Options;

/// <summary>
/// The Files feature's application policy: the declared categories and the ceiling of an upload carried by the API.
/// Bound from <c>Files</c> and validated at start-up.
/// </summary>
public sealed class FilesOptions
{
    /// <summary>The configuration section this type is bound from.</summary>
    public const string SectionName = "Files";

    /// <summary>10 MiB.</summary>
    public const long DefaultApiUploadMaxBytes = 10L * 1024 * 1024;

    /// <summary>
    /// The largest body an upload through the API may carry, kept under the host's 30 MiB request ceiling.
    /// </summary>
    public long ApiUploadMaxBytes { get; set; } = DefaultApiUploadMaxBytes;

    /// <summary>The declared categories, by name. At least one.</summary>
    public Dictionary<string, FileCategoryOptions> Categories { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The settings of <paramref name="category"/>, or null when configuration no longer declares it.
    /// </summary>
    public FileCategoryOptions? Find(FileCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return Categories.GetValueOrDefault(category.Value);
    }
}

/// <summary>
/// Validates a category declaration. Rules that need another section (scanning, the storage provider) belong to the
/// module owning it.
/// </summary>
internal sealed class FilesOptionsValidator : IValidateOptions<FilesOptions>
{
    internal const long MinApiUploadMaxBytes = 1024;

    /// <summary>The host's request body ceiling (<c>RequestLimits:MaxRequestBodyBytes</c>'s maximum).</summary>
    internal const long MaxApiUploadMaxBytes = 30L * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, FilesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        string section = FilesOptions.SectionName;

        if (options.ApiUploadMaxBytes is < MinApiUploadMaxBytes or > MaxApiUploadMaxBytes)
        {
            failures.Add(
                $"'{section}:ApiUploadMaxBytes' must be between {MinApiUploadMaxBytes} and " +
                $"{MaxApiUploadMaxBytes} bytes.");
        }

        if (options.Categories.Count == 0)
        {
            failures.Add($"'{section}:Categories' must declare at least one category.");
        }

        foreach ((string categoryName, var category) in options.Categories)
        {
            ValidateCategory($"{section}:Categories:{categoryName}", categoryName, category, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateCategory(
        string key,
        string categoryName,
        FileCategoryOptions? category,
        List<string> failures)
    {
        if (!FileCategory.IsValid(categoryName))
        {
            failures.Add(
                $"'{key}': a category name is 1 to {FileCategory.MaxLength} characters of lower-case " +
                "letters, digits and '-'.");
        }

        if (category is null)
        {
            failures.Add($"'{key}' declares nothing.");

            return;
        }

        if (category.AllowedMediaTypes.Count == 0)
        {
            failures.Add($"'{key}:AllowedMediaTypes' must name at least one media type.");
        }

        foreach (string mediaType in category.AllowedMediaTypes)
        {
            try
            {
                DeclaredMediaType.Create(mediaType);
            }
            catch (DomainException exception)
            {
                failures.Add($"'{key}:AllowedMediaTypes' holds '{mediaType}': {exception.Message}");
            }
        }

        if (category.MaxSizeInBytes is < FileSize.MinBytes or > FileSize.MaxBytes)
        {
            failures.Add(
                $"'{key}:MaxSizeInBytes' must be between {FileSize.MinBytes} and {FileSize.MaxBytes} bytes.");
        }

        if (!FileCategoryOptions.IsKnownVisibility(category.Visibility))
        {
            failures.Add(
                $"'{key}:Visibility' must be '{FileCategoryOptions.PrivateVisibility}' or " +
                $"'{FileCategoryOptions.AuthenticatedVisibility}'.");
        }

        if (category.UploadModes.Count == 0)
        {
            failures.Add($"'{key}:UploadModes' must name at least one upload mode.");
        }

        foreach (string mode in category.UploadModes.Where(mode => !FileCategoryOptions.IsKnownUploadMode(mode)))
        {
            failures.Add(
                $"'{key}:UploadModes' holds '{mode}', which is neither " +
                $"'{FileCategoryOptions.DirectUploadMode}' nor '{FileCategoryOptions.ApiUploadMode}'.");
        }

        ValidateQuota($"{key}:Quota", category.Quota, failures);
    }

    private static void ValidateQuota(string key, FileCategoryQuotaOptions quota, List<string> failures)
    {
        if (!FileCategoryQuotaOptions.IsKnownMode(quota.Mode))
        {
            failures.Add(
                $"'{key}:Mode' must be '{FileCategoryQuotaOptions.StrictMode}' or " +
                $"'{FileCategoryQuotaOptions.IndicativeMode}'.");
        }

        if (quota.MaxFiles <= 0)
        {
            failures.Add($"'{key}:MaxFiles' must be positive.");
        }

        if (quota.MaxBytes <= 0)
        {
            failures.Add($"'{key}:MaxBytes' must be positive.");
        }

        if (quota.MaxPendingUploads <= 0)
        {
            failures.Add($"'{key}:MaxPendingUploads' must be positive.");
        }
    }
}
