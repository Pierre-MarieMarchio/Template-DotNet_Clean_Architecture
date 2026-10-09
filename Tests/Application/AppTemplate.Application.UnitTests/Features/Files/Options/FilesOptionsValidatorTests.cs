using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Options;

/// <summary>Each refusal of a <c>Files</c> section, proven on its own against an otherwise valid one.</summary>
public sealed class FilesOptionsValidatorTests
{
    private readonly FilesOptionsValidator _validator = new();

    [Fact]
    public void TheTestSettings_AreValid() =>
        _validator.Validate(null, FilesSettings.Default()).Succeeded.ShouldBeTrue();

    [Fact]
    public void NoCategory_IsRefused() =>
        Refused(options => options.Categories.Clear(), "at least one category");

    [Theory]
    [InlineData("Images")]
    [InlineData("my_files")]
    [InlineData("files/2026")]
    public void AMalformedCategoryName_IsRefused(string name) =>
        Refused(options => options.Categories[name] = Image(), "category name");

    [Fact]
    public void ACategoryNameTooLong_IsRefused() =>
        Refused(options => options.Categories[new string('a', FileCategory.MaxLength + 1)] = Image(), "category name");

    [Fact]
    public void ACategoryDeclaringNothing_IsRefused() =>
        Refused(options => options.Categories["empty"] = null!, "declares nothing");

    [Fact]
    public void NoAllowedMediaType_IsRefused() =>
        Refused(options => Image(options).AllowedMediaTypes.Clear(), "AllowedMediaTypes");

    [Theory]
    [InlineData("image/*")]
    [InlineData("*/*")]
    [InlineData("image")]
    [InlineData("")]
    [InlineData("image/png; charset=utf-8")]
    public void AWildcardOrMalformedMediaType_IsRefused(string mediaType) =>
        Refused(options => Image(options).AllowedMediaTypes.Add(mediaType), "AllowedMediaTypes");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FileSize.MaxBytes + 1)]
    public void ASizeOutOfRange_IsRefused(long size) =>
        Refused(options => Image(options).MaxSizeInBytes = size, "MaxSizeInBytes");

    [Fact]
    public void ASizeAtTheDomainCeiling_IsAccepted()
    {
        var options = FilesSettings.With(options => Image(options).MaxSizeInBytes = FileSize.MaxBytes);

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Public")]
    [InlineData("")]
    public void AnUnknownVisibility_IsRefused(string visibility) =>
        Refused(options => Image(options).Visibility = visibility, "Visibility");

    [Fact]
    public void VisibilityAndModes_AreComparedIgnoringCase()
    {
        var options = FilesSettings.With(options =>
        {
            Image(options).Visibility = "authenticated";
            Image(options).UploadModes.Clear();
            Image(options).UploadModes.Add("api");
            Image(options).Quota.Mode = "strict";
        });

        _validator.Validate(null, options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void NoUploadMode_IsRefused() =>
        Refused(options => Image(options).UploadModes.Clear(), "UploadModes");

    [Fact]
    public void AnUnknownUploadMode_IsRefused() =>
        Refused(options => Image(options).UploadModes.Add("Ftp"), "UploadModes");

    [Fact]
    public void AnUnknownQuotaMode_IsRefused() =>
        Refused(options => Image(options).Quota.Mode = "Loose", "Quota:Mode");

    [Fact]
    public void ANonPositiveFileLimit_IsRefused() =>
        Refused(options => Image(options).Quota.MaxFiles = 0, "Quota:MaxFiles");

    [Fact]
    public void ANonPositiveByteLimit_IsRefused() =>
        Refused(options => Image(options).Quota.MaxBytes = 0, "Quota:MaxBytes");

    [Fact]
    public void ANonPositivePendingLimit_IsRefused() =>
        Refused(options => Image(options).Quota.MaxPendingUploads = -1, "Quota:MaxPendingUploads");

    [Theory]
    [InlineData(1023)]
    [InlineData((30L * 1024 * 1024) + 1)]
    public void AnApiUploadCeilingOutOfRange_IsRefused(long bytes) =>
        Refused(options => options.ApiUploadMaxBytes = bytes, "ApiUploadMaxBytes");

    [Fact]
    public void TheDefaultApiUploadCeiling_IsTenMebibytes() =>
        new FilesOptions().ApiUploadMaxBytes.ShouldBe(10L * 1024 * 1024);

    /// <summary>Every failure is reported, not only the first, so one start-up names them all.</summary>
    [Fact]
    public void EveryFailure_IsReported()
    {
        var options = FilesSettings.With(options =>
        {
            Image(options).MaxSizeInBytes = 0;
            Image(options).Quota.MaxFiles = 0;
        });

        _validator.Validate(null, options).Failures!.Count().ShouldBe(2);
    }

    private static FileCategoryOptions Image() =>
        FilesSettings.ACategory(FileCategoryOptions.PrivateVisibility, "image/png");

    private static FileCategoryOptions Image(FilesOptions options) => options.Categories[FilesSettings.Image];

    private void Refused(Action<FilesOptions> change, string mentioning)
    {
        var result = _validator.Validate(null, FilesSettings.With(change));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(mentioning);
    }
}
