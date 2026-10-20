using AppTemplate.Application.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.UnitTests.Features.Files;

internal static class FilesSettings
{
    internal const string Image = "image";
    internal const string Document = "document";

    internal static FilesOptions Default() => With(_ => { });

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
