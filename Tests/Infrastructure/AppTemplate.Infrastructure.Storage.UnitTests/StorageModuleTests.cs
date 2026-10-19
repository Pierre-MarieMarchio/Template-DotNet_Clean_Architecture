using Amazon.S3;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.Common.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Inventories;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Scanners;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Transfers;
using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests;

/// <summary>
/// The module composed the way a host composes it, and reached the way a request reaches it: the
/// adapters are resolved from a scope, never from the root. Scope validation is on, so a lifetime
/// mistake fails here rather than in the first host that composes it.
/// </summary>
public sealed class StorageModuleTests
{
    private static readonly Dictionary<string, string?> _configured = new(StringComparer.Ordinal)
    {
        ["Files:Storage:S3:BucketName"] = "app-files",
        ["Files:Storage:S3:Region"] = "eu-west-3",
        ["Files:Storage:S3:Endpoint"] = "http://localhost:9000",
        ["Files:Storage:S3:ForcePathStyle"] = "true",
        ["Files:Storage:S3:AccessKeyId"] = "test-access-key-id",
        ["Files:Storage:S3:SecretAccessKey"] = "test-secret-access-key",
        ["Files:Storage:S3:MaxGrantLifetime"] = "00:20:00",
    };

    /// <summary>S3 is the provider a configuration that names none gets.</summary>
    [Fact]
    public void AddStorageModule_ServesTheS3AdaptersByDefault()
    {
        using var provider = Compose(_configured);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFileContentStore>().ShouldBeOfType<S3FileContentStore>();
        scope.ServiceProvider.GetRequiredService<IFileContentInventory>().ShouldBeOfType<S3FileContentInventory>();
        scope.ServiceProvider.GetRequiredService<IFileDirectTransfer>().IsSupported.ShouldBeTrue();
    }

    [Fact]
    public void AddStorageModule_ServesTheFileSystemAdaptersWhenThatProviderIsChosen()
    {
        using var provider = Compose(FileSystemSettings());
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFileContentStore>().ShouldBeOfType<FileSystemFileContentStore>();
        scope.ServiceProvider.GetRequiredService<IFileContentInventory>()
            .ShouldBeOfType<FileSystemFileContentInventory>();
        scope.ServiceProvider.GetRequiredService<IFileDirectTransfer>().ShouldBeOfType<FileSystemFileDirectTransfer>()
            .IsSupported.ShouldBeFalse();
    }

    /// <summary>
    /// Only the chosen provider's section is validated: a file system deployment has no bucket, and
    /// demanding one would fail its boot over a store it does not use.
    /// </summary>
    [Fact]
    public void AddStorageModule_NeitherValidatesNorBuildsS3ForTheFileSystemProvider()
    {
        using var provider = Compose(FileSystemSettings());

        provider.GetService<IAmazonS3>().ShouldBeNull();
        provider.GetServices<IValidateOptions<FilesStorageS3Options>>().ShouldBeEmpty();
    }

    [Fact]
    public void AddStorageModule_RefusesTheFileSystemProviderWithARelativeRoot()
    {
        var settings = FileSystemSettings();
        settings["Files:Storage:FileSystem:RootPath"] = "relative/files";

        using var provider = Compose(settings);

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<FilesStorageFileSystemOptions>>().Value);
    }

    /// <summary>An unknown provider is refused at start-up rather than quietly served by another.</summary>
    [Fact]
    public void AddStorageModule_RefusesAnUnknownProvider()
    {
        var settings = new Dictionary<string, string?>(_configured, StringComparer.Ordinal)
        {
            ["Files:Storage:Provider"] = "Ftp",
        };

        using var provider = Compose(settings);

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<FilesStorageOptions>>().Value);
    }

    [Fact]
    public void AddStorageModule_FindsNothingWhenScanningIsDisabled()
    {
        using var provider = Compose(_configured);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFileContentScanner>().ShouldBeOfType<NoOpFileContentScanner>();
    }

    [Fact]
    public void AddStorageModule_ScansWithClamAvWhenScanningIsEnabled()
    {
        var settings = new Dictionary<string, string?>(_configured, StringComparer.Ordinal)
        {
            ["Files:Scanning:Enabled"] = "true",
            ["Files:Scanning:Host"] = "clamav",
        };

        // The adapter logs; a host always has logging, this bare composition does not.
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddStorageModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFileContentScanner>().ShouldBeOfType<ClamAvFileContentScanner>();
    }

    [Fact]
    public void AddStorageModule_RefusesScanningEnabledWithNoHost()
    {
        var settings = new Dictionary<string, string?>(_configured, StringComparer.Ordinal)
        {
            ["Files:Scanning:Enabled"] = "true",
        };

        using var provider = Compose(settings);

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<FilesScanningOptions>>().Value);
    }

    /// <summary>
    /// One client for the process. It owns a connection pool and a retry schedule, and a second one
    /// would silently double both.
    /// </summary>
    [Fact]
    public void AddStorageModule_ServesOneClientForEveryScope()
    {
        using var provider = Compose(_configured);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<IAmazonS3>()
            .ShouldBeSameAs(second.ServiceProvider.GetRequiredService<IAmazonS3>());
    }

    /// <summary>
    /// The presigning client is a singleton too, and a second registration of the same interface does
    /// not disturb the first: what asks for <see cref="IAmazonS3"/> without a key still gets the one
    /// this process talks to the store with.
    /// </summary>
    [Fact]
    public void AddStorageModule_ServesOneSigningClientForEveryScope()
    {
        using var provider = Compose(_configured);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredKeyedService<IAmazonS3>(BucketClientFactory.SigningClientKey)
            .ShouldBeSameAs(
                second.ServiceProvider.GetRequiredKeyedService<IAmazonS3>(BucketClientFactory.SigningClientKey));
    }

    /// <summary>
    /// Two clients, one endpoint each, and the right one under each name. They differ by host alone,
    /// so swapping them compiles and fails only where the public name does not resolve from inside —
    /// which is every deployment this setting exists for.
    /// </summary>
    [Fact]
    public void AddStorageModule_BuildsThePresigningClientOnThePublicEndpoint()
    {
        var published = new Dictionary<string, string?>(_configured, StringComparer.Ordinal)
        {
            ["Files:Storage:S3:Endpoint"] = "http://minio:9000",
            ["Files:Storage:S3:PublicEndpoint"] = "https://files.example",
        };

        using var provider = Compose(published);

        ServiceUrlOf(provider.GetRequiredService<IAmazonS3>()).ShouldBe("http://minio:9000/");
        ServiceUrlOf(provider.GetRequiredKeyedService<IAmazonS3>(BucketClientFactory.SigningClientKey))
            .ShouldBe("https://files.example/");
    }

    /// <summary>
    /// <b>The compatibility promise, composed the way a host composes it.</b> A configuration with no
    /// <c>Files:Storage:S3:PublicEndpoint</c> key at all — which is every deployment that predates the setting
    /// — signs for the endpoint it always signed for.
    /// </summary>
    [Fact]
    public void AddStorageModule_SignsForTheEndpointItselfWhenTheConfigurationNamesNoPublicOne()
    {
        _configured.ShouldNotContainKey(
            "Files:Storage:S3:PublicEndpoint",
            "this test is about a file that does not mention the key; supplying it here would make " +
            "the assertion below true for the wrong reason.");

        using var provider = Compose(_configured);

        var options = provider.GetRequiredService<IOptions<FilesStorageS3Options>>().Value;

        options.PublicEndpoint.ShouldBeEmpty();
        options.SigningEndpoint.ShouldBe(options.Endpoint);

        ServiceUrlOf(provider.GetRequiredKeyedService<IAmazonS3>(BucketClientFactory.SigningClientKey))
            .ShouldBe(ServiceUrlOf(provider.GetRequiredService<IAmazonS3>()));
    }

    [Fact]
    public void AddStorageModule_BindsTheSectionAnOperatorWrites()
    {
        using var provider = Compose(_configured);

        var options = provider.GetRequiredService<IOptions<FilesStorageS3Options>>().Value;

        options.BucketName.ShouldBe("app-files");
        options.Endpoint.ShouldBe("http://localhost:9000");
        options.ForcePathStyle.ShouldBeTrue();
        options.MaxGrantLifetime.ShouldBe(TimeSpan.FromMinutes(20));
    }

    /// <summary>
    /// The validator is wired to the options, not merely registered: an incomplete configuration has
    /// to be refused where it is read, which under <c>ValidateOnStart</c> is before the host serves
    /// anything.
    /// </summary>
    [Fact]
    public void AddStorageModule_RefusesAConfigurationWithNoBucket()
    {
        var incomplete = new Dictionary<string, string?>(_configured, StringComparer.Ordinal)
        {
            ["Files:Storage:S3:BucketName"] = string.Empty,
        };

        using var provider = Compose(incomplete);

        var failure = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<FilesStorageS3Options>>().Value);

        failure.Failures.ShouldContain(message => message.Contains("BucketName", StringComparison.Ordinal));
    }

    [Fact]
    public void AddStorageModule_ThrowsWhenThereIsNoContainerToComposeInto()
    {
        Should.Throw<ArgumentNullException>(
            () => StorageModule.AddStorageModule(services: null!, new ConfigurationBuilder().Build()));
    }

    [Fact]
    public void AddStorageModule_ThrowsWhenThereIsNoConfigurationToBind()
    {
        Should.Throw<ArgumentNullException>(
            () => new ServiceCollection().AddStorageModule(configuration: null!));
    }

    [Fact]
    public void AddStorageModule_ReturnsTheCollectionItWasGiven()
    {
        var services = new ServiceCollection();

        services.AddStorageModule(new ConfigurationBuilder().Build()).ShouldBeSameAs(services);
    }

    private static Dictionary<string, string?> FileSystemSettings() =>
        new(StringComparer.Ordinal)
        {
            ["Files:Storage:Provider"] = "FileSystem",
            ["Files:Storage:FileSystem:RootPath"] = Path.Combine(Path.GetTempPath(), "app-files"),
        };

    private static string? ServiceUrlOf(IAmazonS3 client) => ((AmazonS3Config)client.Config).ServiceURL;

    private static ServiceProvider Compose(Dictionary<string, string?> settings) =>
        new ServiceCollection()
            .AddStorageModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider(validateScopes: true);
}
