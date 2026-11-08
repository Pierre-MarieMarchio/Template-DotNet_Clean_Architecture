using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Configuration.Options;

public sealed class FilesCategoriesValidatorTests
{
    [Fact]
    public void AScannedCategory_IsRefusedWhileScanningIsDisabled()
    {
        var result = ScanningValidator(enabled: false).Validate(null, Categories(scan: true));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Files:Categories:document:Scan");
    }

    [Fact]
    public void AScannedCategory_IsAcceptedWhileScanningIsEnabled() =>
        ScanningValidator(enabled: true).Validate(null, Categories(scan: true)).Succeeded.ShouldBeTrue();

    [Fact]
    public void AnUnscannedCategory_IsAcceptedWhileScanningIsDisabled() =>
        ScanningValidator(enabled: false).Validate(null, Categories(scan: false)).Succeeded.ShouldBeTrue();

    [Fact]
    public void ADirectOnlyCategory_IsRefusedByTheFileSystemStore()
    {
        var result = StorageValidator(FilesStorageOptions.FileSystemProvider)
            .Validate(null, Categories(modes: [FileCategoryOptions.DirectUploadMode]));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Files:Categories:document:UploadModes");
    }

    [Fact]
    public void ACategoryAlsoUploadedThroughTheApi_IsAcceptedByTheFileSystemStore() =>
        StorageValidator(FilesStorageOptions.FileSystemProvider)
            .Validate(null, Categories(modes: [FileCategoryOptions.DirectUploadMode, FileCategoryOptions.ApiUploadMode]))
            .Succeeded.ShouldBeTrue();

    [Fact]
    public void ADirectOnlyCategory_IsAcceptedByTheS3Store() =>
        StorageValidator(FilesStorageOptions.S3Provider)
            .Validate(null, Categories(modes: [FileCategoryOptions.DirectUploadMode]))
            .Succeeded.ShouldBeTrue();

    private static FilesCategoriesScanningValidator ScanningValidator(bool enabled) =>
        new(Microsoft.Extensions.Options.Options.Create(new FilesScanningOptions { Enabled = enabled, Host = "clamav" }));

    private static FilesCategoriesStorageValidator StorageValidator(string provider) =>
        new(Microsoft.Extensions.Options.Options.Create(new FilesStorageOptions { Provider = provider }));

    private static FilesOptions Categories(bool scan = false, string[]? modes = null)
    {
        var category = new FileCategoryOptions { MaxSizeInBytes = 1024, Scan = scan };
        category.AllowedMediaTypes.Add("application/pdf");
        category.UploadModes.AddRange(modes ?? [FileCategoryOptions.DirectUploadMode]);

        var options = new FilesOptions();
        options.Categories["document"] = category;

        return options;
    }
}
