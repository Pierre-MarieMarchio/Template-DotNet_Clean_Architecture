using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.UseCases.Commands.RegisterFile;

public sealed class RegisterFileCommandValidatorTests
{
    private readonly RegisterFileCommandValidator _validator = new(FilesSettings.Default().AsOptions());

    /// <summary>
    /// The reason every rule here carries <c>Cascade(CascadeMode.Stop)</c>: FluentValidation runs
    /// the remaining rules for a property even after <c>NotEmpty</c> has failed, and each of the
    /// <c>Must</c> rules dereferences the value. Removing one of those cascades turns this from a
    /// validation failure into a <c>NullReferenceException</c>.
    /// </summary>
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

    /// <summary>
    /// Measured after the domain's own normalisation. A name at the maximum followed by trailing
    /// spaces and dots is one <see cref="StoredFileName.Create"/> accepts, so refusing it here would
    /// reject a request the domain would have taken.
    /// </summary>
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

    /// <summary>A name that is nothing but dots and spaces normalises to nothing at all.</summary>
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

    /// <summary>
    /// The ceiling is the protocol's own limit on a single-part deposit, and this feature offers
    /// exactly one part — so accepting a larger registration would promise an upload that cannot
    /// physically be made.
    /// </summary>
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

    /// <summary>Compared after the domain's normalisation, so casing and padding do not refuse a type.</summary>
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

    /// <summary>An unknown category is reported once, not again as a refused type and size.</summary>
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
