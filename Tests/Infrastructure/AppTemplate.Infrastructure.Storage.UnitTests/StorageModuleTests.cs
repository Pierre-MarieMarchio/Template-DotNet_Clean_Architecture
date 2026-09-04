using Amazon.S3;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Buckets;
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
        ["Storage:BucketName"] = "app-files",
        ["Storage:Region"] = "eu-west-3",
        ["Storage:Endpoint"] = "http://localhost:9000",
        ["Storage:ForcePathStyle"] = "true",
        ["Storage:AccessKeyId"] = "test-access-key-id",
        ["Storage:SecretAccessKey"] = "test-secret-access-key",
        ["Storage:MaxGrantLifetime"] = "00:20:00",
    };

    [Fact]
    public void AddStorageModule_ServesBothFilePortsFromAScope()
    {
        using var provider = Compose(_configured);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFileContentStore>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IFileContentInventory>().ShouldNotBeNull();
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
            ["Storage:Endpoint"] = "http://minio:9000",
            ["Storage:PublicEndpoint"] = "https://files.example",
        };

        using var provider = Compose(published);

        ServiceUrlOf(provider.GetRequiredService<IAmazonS3>()).ShouldBe("http://minio:9000/");
        ServiceUrlOf(provider.GetRequiredKeyedService<IAmazonS3>(BucketClientFactory.SigningClientKey))
            .ShouldBe("https://files.example/");
    }

    /// <summary>
    /// <b>The compatibility promise, composed the way a host composes it.</b> A configuration with no
    /// <c>Storage:PublicEndpoint</c> key at all — which is every deployment that predates the setting
    /// — signs for the endpoint it always signed for.
    /// </summary>
    [Fact]
    public void AddStorageModule_SignsForTheEndpointItselfWhenTheConfigurationNamesNoPublicOne()
    {
        _configured.ShouldNotContainKey(
            "Storage:PublicEndpoint",
            "this test is about a file that does not mention the key; supplying it here would make " +
            "the assertion below true for the wrong reason.");

        using var provider = Compose(_configured);

        var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

        options.PublicEndpoint.ShouldBeEmpty();
        options.SigningEndpoint.ShouldBe(options.Endpoint);

        ServiceUrlOf(provider.GetRequiredKeyedService<IAmazonS3>(BucketClientFactory.SigningClientKey))
            .ShouldBe(ServiceUrlOf(provider.GetRequiredService<IAmazonS3>()));
    }

    [Fact]
    public void AddStorageModule_BindsTheSectionAnOperatorWrites()
    {
        using var provider = Compose(_configured);

        var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;

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
            ["Storage:BucketName"] = string.Empty,
        };

        using var provider = Compose(incomplete);

        var failure = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<StorageOptions>>().Value);

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

    private static string? ServiceUrlOf(IAmazonS3 client) => ((AmazonS3Config)client.Config).ServiceURL;

    private static ServiceProvider Compose(Dictionary<string, string?> settings) =>
        new ServiceCollection()
            .AddStorageModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider(validateScopes: true);
}
