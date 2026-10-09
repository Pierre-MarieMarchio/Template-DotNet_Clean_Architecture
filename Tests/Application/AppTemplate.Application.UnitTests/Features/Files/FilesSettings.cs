using AppTemplate.Application.Features.Files.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.UnitTests.Features.Files;

/// <summary>
/// The <c>Files</c> configuration the use-case tests run against: an <c>image</c> category open to
/// every signed-in user, and a private <c>document</c> category, both direct and scanned, with the
/// default quota.
/// </summary>
internal static class FilesSettings
{
    internal const string Image = "image";
    internal const string Document = "document";

    internal static FilesOptions Default() => With(_ => { });

    /// <summary>The default settings, with <paramref name="change"/> applied to them.</summary>
    internal static FilesOptions With(Action<FilesOptions> change)
    {
        var options = new FilesOptions();

        options.Categories[Image] = ACategory(
            FileCategoryOptions.AuthenticatedVisibility,
            "image/png",
            "image/jpeg");
        options.Categories[Document] = ACategory(
            FileCategoryOptions.PrivateVisibility,
            "application/pdf",
            "text/csv");

        change(options);

        return options;
    }

    internal static IOptions<FilesOptions> AsOptions(this FilesOptions options) =>
        Microsoft.Extensions.Options.Options.Create(options);

    internal static FileCategoryOptions ACategory(string visibility, params string[] mediaTypes)
    {
        var category = new FileCategoryOptions
        {
            MaxSizeInBytes = 10L * 1024 * 1024,
            Scan = true,
            VerifySignature = true,
            Visibility = visibility,
        };

        category.AllowedMediaTypes.AddRange(mediaTypes);
        category.UploadModes.AddRange([FileCategoryOptions.DirectUploadMode, FileCategoryOptions.ApiUploadMode]);

        return category;
    }
}
