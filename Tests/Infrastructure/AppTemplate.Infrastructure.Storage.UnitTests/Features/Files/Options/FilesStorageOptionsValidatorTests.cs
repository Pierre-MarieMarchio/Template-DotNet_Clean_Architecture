using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Options;

public sealed class FilesStorageOptionsValidatorTests
{
    [Fact]
    public void TheDefaultProvider_IsS3() =>
        new FilesStorageOptions().Provider.ShouldBe("S3");

    [Theory]
    [InlineData("S3")]
    [InlineData("FileSystem")]
    [InlineData("filesystem")]
    public void AKnownProvider_IsValid(string provider) =>
        new FilesStorageOptionsValidator()
            .Validate(null, new FilesStorageOptions { Provider = provider })
            .Succeeded.ShouldBeTrue();

    [Fact]
    public void AnUndefinedProvider_IsRefused()
    {
        var result = new FilesStorageOptionsValidator()
            .Validate(null, new FilesStorageOptions { Provider = "Ftp" });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Files:Storage:Provider");
    }

    [Fact]
    public void AnAbsoluteRoot_IsValid() =>
        new FilesStorageFileSystemOptionsValidator()
            .Validate(null, new FilesStorageFileSystemOptions { RootPath = Path.GetTempPath() })
            .Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AMissingRoot_IsRefused(string rootPath)
    {
        var result = new FilesStorageFileSystemOptionsValidator()
            .Validate(null, new FilesStorageFileSystemOptions { RootPath = rootPath });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Files:Storage:FileSystem:RootPath");
    }

    /// <summary>
    /// A relative root resolves against the working directory, so the store would move whenever the
    /// process is started from somewhere else.
    /// </summary>
    [Theory]
    [InlineData("files")]
    [InlineData("./files")]
    [InlineData("../files")]
    public void ARelativeRoot_IsRefused(string rootPath)
    {
        var result = new FilesStorageFileSystemOptionsValidator()
            .Validate(null, new FilesStorageFileSystemOptions { RootPath = rootPath });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("absolute");
    }
}
