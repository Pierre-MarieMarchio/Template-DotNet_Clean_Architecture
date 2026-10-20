using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.RegisterFile;

public sealed class RegisterFileCommandValidatorTests
{
    private readonly RegisterFileCommandValidator _validator = new(FilesSettings.Default().AsOptions());

    [Fact]
    public async Task ANullName_IsAValidationFailureRatherThanADereference()
    {
        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(FilesSettings.Image, null!, "image/png", 1_024, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ANullChecksum_IsAValidationFailureRatherThanADereference()
    {
        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(FilesSettings.Image, "holiday.png", "image/png", 1_024, null!),
            TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ANullMediaType_IsAValidationFailureRatherThanADereference()
    {
        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(FilesSettings.Image, "holiday.png", null!, 1_024, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ANameAtTheLimit_WithTrailingSpaceAndDot_IsAccepted()
    {
        var command = new RegisterFileCommand(
            FilesSettings.Image,
            new string('a', StoredFileName.MaxLength) + " . ",
            "image/png",
            1_024,
            AStoredFile.Checksum);

        var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ANameOneCharacterTooLong_IsRefused()
    {
        var command = new RegisterFileCommand(
            FilesSettings.Image,
            new string('a', StoredFileName.MaxLength + 1),
            "image/png",
            1_024,
            AStoredFile.Checksum);

        var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ANameOfDotsAndSpaces_IsRefused()
    {
        var command = new RegisterFileCommand(FilesSettings.Image, " .. ", "image/png", 1_024, AStoredFile.Checksum);

        var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ASizeBelowTheFloor_IsRefused(long sizeInBytes)
    {
        var command = new RegisterFileCommand(FilesSettings.Image, "holiday.png", "image/png", sizeInBytes, AStoredFile.Checksum);

        var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ASizeAboveTheSinglePartCeiling_IsRefused()
    {
        var command = new RegisterFileCommand(
            FilesSettings.Image,
            "holiday.png",
            "image/png",
            FileSize.MaxBytes + 1,
            AStoredFile.Checksum);

        var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task AWellFormedCommand_IsAccepted()
    {
        var command = new RegisterFileCommand(FilesSettings.Image, "holiday.png", "image/png", 1_024, AStoredFile.Checksum);

        var result = await _validator.ValidateAsync(command, TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue();
    }

    #region The category's rules

    [Fact]
    public async Task ANullCategory_IsAValidationFailureRatherThanADereference()
    {
        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(null!, "holiday.png", "image/png", 1_024, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldContain(error => error.PropertyName == nameof(RegisterFileCommand.Category));
    }

    [Theory]
    [InlineData("videos")]
    [InlineData("Image")]
    [InlineData("image ")]
    public async Task ACategoryTheDeploymentDoesNotDeclare_IsRefusedOnItsField(string category)
    {
        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(category, "holiday.png", "image/png", 1_024, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(RegisterFileCommand.Category));
    }

    [Fact]
    public async Task AMediaTypeTheCategoryDoesNotAllow_IsRefusedOnItsField()
    {
        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(FilesSettings.Image, "report.pdf", "application/pdf", 1_024, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(RegisterFileCommand.MediaType));
    }

    [Fact]
    public async Task AnAllowedMediaTypeInAnotherCase_IsAccepted()
    {
        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(FilesSettings.Image, "holiday.png", " IMAGE/PNG ", 1_024, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task ASizeAboveTheCategoryMaximum_IsRefusedOnItsField()
    {
        long maximum = FilesSettings.Default().Categories[FilesSettings.Image].MaxSizeInBytes;

        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(FilesSettings.Image, "holiday.png", "image/png", maximum + 1, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(RegisterFileCommand.SizeInBytes));
    }

    [Fact]
    public async Task ASizeAtTheCategoryMaximum_IsAccepted()
    {
        long maximum = FilesSettings.Default().Categories[FilesSettings.Image].MaxSizeInBytes;

        var result = await _validator.ValidateAsync(
            new RegisterFileCommand(FilesSettings.Image, "holiday.png", "image/png", maximum, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUnknownCategory_IsNotReportedAgainOnTheTypeOrTheSize()
    {
        var result = await _validator.ValidateAsync(
            new RegisterFileCommand("videos", "holiday.mp4", "video/mp4", FileSize.MaxBytes, AStoredFile.Checksum),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().PropertyName.ShouldBe(nameof(RegisterFileCommand.Category));
    }

    #endregion
}
