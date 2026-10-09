using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Options;

/// <summary>
/// One category of <c>Files:Categories</c>: what a file registered under it may be, how its content
/// is inspected, who may read it, how it is uploaded and how much of it one owner may hold.
/// </summary>
public sealed class FileCategoryOptions
{
    /// <summary>Only the owner reads the content.</summary>
    public const string PrivateVisibility = "Private";

    /// <summary>Any signed-in user may download the content of an available file by its id.</summary>
    public const string AuthenticatedVisibility = "Authenticated";

    /// <summary>The client deposits the bytes on the store through a signed URL.</summary>
    public const string DirectUploadMode = "Direct";

    /// <summary>The client sends the bytes to the API, which writes them to the store.</summary>
    public const string ApiUploadMode = "Api";

    /// <summary>The media types a file of this category may declare, matched exactly after normalisation.</summary>
    public List<string> AllowedMediaTypes { get; } = [];

    /// <summary>The largest file of this category, at most <see cref="FileSize.MaxBytes"/>.</summary>
    public long MaxSizeInBytes { get; set; }

    /// <summary>Whether deposited content is sent to the malware scanner. Requires <c>Files:Scanning:Enabled</c>.</summary>
    public bool Scan { get; set; }

    /// <summary>
    /// Whether the leading bytes must agree with the declared media type. Markup that can carry a
    /// script is refused whatever this says.
    /// </summary>
    public bool VerifySignature { get; set; } = true;

    /// <summary><see cref="PrivateVisibility"/> or <see cref="AuthenticatedVisibility"/>, compared ignoring case.</summary>
    public string Visibility { get; set; } = PrivateVisibility;

    /// <summary><see cref="DirectUploadMode"/> and/or <see cref="ApiUploadMode"/>, compared ignoring case. At least one.</summary>
    public List<string> UploadModes { get; } = [];

    /// <summary>How much of this category one owner may hold.</summary>
    public FileCategoryQuotaOptions Quota { get; } = new();

    /// <summary>Whether any signed-in user may download an available file of this category.</summary>
    public bool IsReadableByAnySignedInUser =>
        string.Equals(Visibility, AuthenticatedVisibility, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a file of this category may be registered for a direct upload.</summary>
    public bool AllowsDirectUpload =>
        UploadModes.Any(mode => string.Equals(mode, DirectUploadMode, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="mediaType"/> is one this category accepts.</summary>
    public bool Allows(DeclaredMediaType mediaType)
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        return AllowedMediaTypes.Any(allowed =>
            string.Equals(allowed.Trim(), mediaType.Value, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsKnownVisibility(string? visibility) =>
        string.Equals(visibility, PrivateVisibility, StringComparison.OrdinalIgnoreCase)
        || string.Equals(visibility, AuthenticatedVisibility, StringComparison.OrdinalIgnoreCase);

    internal static bool IsKnownUploadMode(string? mode) =>
        string.Equals(mode, DirectUploadMode, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mode, ApiUploadMode, StringComparison.OrdinalIgnoreCase);
}
