using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.Features.Files.Configuration.Options;

public sealed class FileCategoryOptions
{
    public const string PrivateVisibility = "Private";

    public const string AuthenticatedVisibility = "Authenticated";

    public const string DirectUploadMode = "Direct";

    public const string ApiUploadMode = "Api";

    public List<string> AllowedMediaTypes { get; } = [];

    public long MaxSizeInBytes { get; set; }

    public bool Scan { get; set; }

    public bool VerifySignature { get; set; } = true;

    public string Visibility { get; set; } = PrivateVisibility;

    public List<string> UploadModes { get; } = [];

    public FileCategoryQuotaOptions Quota { get; } = new();

    public bool IsReadableByAnySignedInUser =>
        string.Equals(Visibility, AuthenticatedVisibility, StringComparison.OrdinalIgnoreCase);

    public bool AllowsDirectUpload =>
        UploadModes.Any(mode => string.Equals(mode, DirectUploadMode, StringComparison.OrdinalIgnoreCase));

    public bool AllowsApiUpload =>
        UploadModes.Any(mode => string.Equals(mode, ApiUploadMode, StringComparison.OrdinalIgnoreCase));

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
