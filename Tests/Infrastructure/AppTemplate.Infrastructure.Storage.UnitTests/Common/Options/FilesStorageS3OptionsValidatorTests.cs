using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Common.Options;

public sealed class FilesStorageS3OptionsValidatorTests
{
    private readonly FilesStorageS3OptionsValidator _validator = new();

    [Fact]
    public void ADeployedConfiguration_IsAccepted() => Validate(StorageFixture.Options()).Succeeded.ShouldBeTrue();

    [Fact]
    public void AConfigurationWithNoCredentials_IsAccepted()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.AccessKeyId = string.Empty;
            storage.SecretAccessKey = string.Empty;
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABucketWithNoName_IsRefused(string bucketName) =>
        Failures(storage => storage.BucketName = bucketName).ShouldContain(failure => failure.Contains("BucketName"));

    [Theory]
    [InlineData("ab")]
    [InlineData("App-Files")]
    [InlineData("app_files")]
    [InlineData("-app-files")]
    [InlineData("app-files-")]
    public void AMalformedBucketName_IsRefused(string bucketName) =>
        Failures(storage => storage.BucketName = bucketName).ShouldContain(failure => failure.Contains("BucketName"));

    [Fact]
    public void ARegionThatIsNotStated_IsRefused() =>
        Failures(storage => storage.Region = " ").ShouldContain(failure => failure.Contains("Region"));

    [Theory]
    [InlineData("objectstore:9000")]
    [InlineData("ftp://objects.example")]
    [InlineData("/objects")]
    public void AnEndpointThatIsNotAnHttpUrl_IsRefused(string endpoint) =>
        Failures(storage => storage.Endpoint = endpoint).ShouldContain(failure => failure.Contains("Endpoint"));

    [Fact]
    public void APlaintextEndpointAgainstAHostThatIsNotLoopback_IsRefused()
    {
        Failures(storage => storage.Endpoint = "http://objectstore:9000")
            .ShouldContain(failure => failure.Contains("clear text"));
    }

    [Fact]
    public void APlaintextEndpointAgainstLoopback_IsAccepted()
    {
        Validate(StorageFixture.Options(storage => storage.Endpoint = "http://localhost:9000"))
            .Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void APlaintextEndpointAcceptedDeliberately_IsAllowed()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://objectstore:9000";
            storage.AllowInsecureTransport = true;
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void AnHttpsEndpoint_NeedsNoPermission()
    {
        Validate(StorageFixture.Options(storage => storage.Endpoint = "https://objects.example"))
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("objectstore:9000")]
    [InlineData("ftp://objects.example")]
    [InlineData("/objects")]
    public void APublicEndpointThatIsNotAnHttpUrl_IsRefused(string publicEndpoint)
    {
        Failures(storage => storage.PublicEndpoint = publicEndpoint)
            .ShouldContain(failure => failure.Contains("PublicEndpoint"));
    }

    [Fact]
    public void APlaintextPublicEndpointBehindAnEncryptedInternalOne_IsRefused()
    {
        var failures = Failures(storage =>
        {
            storage.Endpoint = "https://objectstore.internal";
            storage.PublicEndpoint = "http://files.example";
        });

        failures.ShouldContain(failure => failure.Contains("clear text"));
        failures.ShouldContain(failure => failure.Contains($"'{FilesStorageS3Options.SectionName}:PublicEndpoint'"));
    }

    [Fact]
    public void APlaintextInternalEndpointUnderAnEncryptedPublicOne_NeedsNoPermission()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://objectstore:9000";
            storage.PublicEndpoint = "https://files.example";
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void APlaintextPublicEndpointAcceptedDeliberately_IsAllowed()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "https://objectstore.internal";
            storage.PublicEndpoint = "http://files.example";
            storage.AllowInsecureTransport = true;
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void APlaintextPublicEndpointAgainstLoopback_IsAccepted()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://objectstore:9000";
            storage.PublicEndpoint = "http://localhost:9000";
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void TheClearTextFailure_NamesTheKeyThatCarriesTheEndpointItIsAbout()
    {
        Failures(storage => storage.Endpoint = "http://objectstore:9000")
            .ShouldContain(failure => failure.Contains($"'{FilesStorageS3Options.SectionName}:Endpoint' is an http URL"));

        Failures(storage =>
            {
                storage.Endpoint = "http://objectstore:9000";
                storage.PublicEndpoint = "http://files.example";
            })
            .ShouldContain(failure =>
                failure.Contains($"'{FilesStorageS3Options.SectionName}:PublicEndpoint' is an http URL"));
    }

    [Theory]
    [InlineData("http://objectstore:9000", false, false)]
    [InlineData("http://objectstore:9000", true, true)]
    [InlineData("http://localhost:9000", false, true)]
    [InlineData("https://objects.example", false, true)]
    [InlineData("", false, true)]
    public void AConfigurationWithNoPublicEndpoint_IsJudgedExactlyAsItWasBefore(
        string endpoint,
        bool allowInsecure,
        bool expected)
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = endpoint;
            storage.AllowInsecureTransport = allowInsecure;
        });

        options.PublicEndpoint.ShouldBe(
            string.Empty,
            "the default has to be absent, or every deployment that predates the setting acquires one.");

        Validate(options).Succeeded.ShouldBe(expected);
    }

    [Fact]
    public void AKeyIdWithNoSecret_IsRefused()
    {
        Failures(storage => storage.SecretAccessKey = string.Empty)
            .ShouldContain(failure => failure.Contains("SecretAccessKey"));
    }

    [Fact]
    public void ASecretWithNoKeyId_IsRefused()
    {
        Failures(storage => storage.AccessKeyId = string.Empty)
            .ShouldContain(failure => failure.Contains("AccessKeyId"));
    }

    [Fact]
    public void AGrantCeilingOfZero_IsRefused()
    {
        Failures(storage => storage.MaxGrantLifetime = TimeSpan.Zero)
            .ShouldContain(failure => failure.Contains("MaxGrantLifetime"));
    }

    [Fact]
    public void AGrantCeilingLongerThanASignatureCanCarry_IsRefused()
    {
        Failures(storage => storage.MaxGrantLifetime = FilesStorageS3Options.MaxSignableLifetime + TimeSpan.FromSeconds(1))
            .ShouldContain(failure => failure.Contains("MaxGrantLifetime"));
    }

    [Fact]
    public void EveryFailure_NamesItsConfigurationKeyInFull()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.BucketName = string.Empty;
            storage.Region = string.Empty;
        });

        Validate(options).Failures.ShouldNotBeNull().ShouldAllBe(failure => failure.Contains($"'{FilesStorageS3Options.SectionName}:"));
    }

    private IEnumerable<string> Failures(Action<FilesStorageS3Options> adjust)
    {
        var result = Validate(StorageFixture.Options(adjust));

        result.Failed.ShouldBeTrue();

        return result.Failures ?? [];
    }

    private ValidateOptionsResult Validate(FilesStorageS3Options options) =>
        _validator.Validate(name: null, options);
}
