using System.Globalization;
using System.Net.Http.Headers;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using AppTemplate.Infrastructure.Storage;
using AppTemplate.Infrastructure.Storage.Common.Options;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

public sealed class ObjectStoreFixture : IAsyncLifetime
{
    private const string _storeImage =
        "chrislusf/seaweedfs:4.50@sha256:c2658820e763294a9e9de7b36c81e59b879e3459148351aee4e31a126ca64860";

    private const ushort _s3Port = 9000;

    private const string _accessKeyId = "apptemplate-tests";

    private const string _secretAccessKey = "apptemplate-tests-secret";

    private const string _bucketName = "apptemplate-storage-tests";

    private readonly IContainer _container = new ContainerBuilder(_storeImage)
        .WithEnvironment("AWS_ACCESS_KEY_ID", _accessKeyId)
        .WithEnvironment("AWS_SECRET_ACCESS_KEY", _secretAccessKey)
        .WithCommand(
            "mini",
            "-dir=/data",
            $"-s3.port={_s3Port}",
            "-master.telemetry=false",
            "-admin.ui=false",
            "-webdav=false",
            "-s3.port.iceberg=0",
            "-s3.port.lance=0",
            "-s3.autoCreateBucket=false")
        .WithPortBinding(_s3Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(_s3Port).ForPath("/healthz")))
        .Build();

    private ServiceProvider? _host;
    private AsyncServiceScope _scope;

    public IFileContentStore Content => Resolve<IFileContentStore>();

    public IFileDirectTransfer Transfer => Resolve<IFileDirectTransfer>();

    public IFileContentInventory Inventory => Resolve<IFileContentInventory>();

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;

        await _container.StartAsync(token);

        var services = new ServiceCollection();

        services.AddHttpClient();
        services.AddStorageModule(Configuration());

        _host = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        _scope = _host.CreateAsyncScope();

        await _host.GetRequiredService<IAmazonS3>()
            .PutBucketAsync(new PutBucketRequest { BucketName = _bucketName }, token);
    }

    public static string KeyUnder(string prefix) =>
        $"{prefix}/{Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)}";

    public static string UniquePrefix(string purpose) =>
        $"{purpose}-{Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[..12]}";

    public async Task<HttpResponseMessage> DepositAsync(
        IssuedUploadGrant grant,
        byte[] payload,
        CancellationToken cancellationToken,
        string? hostHeader = null)
    {
        ArgumentNullException.ThrowIfNull(grant);

        using var content = new ByteArrayContent(payload);

        foreach (var header in grant.RequiredHeaders)
        {
            Apply(content, header.Key, header.Value);
        }

        using var request = new HttpRequestMessage(new HttpMethod(grant.Method), grant.Url)
        {
            Content = content,
        };

        if (hostHeader is not null)
        {
            request.Headers.Host = hostHeader;
        }

        return await Send(request, cancellationToken);
    }

    public async Task<HttpResponseMessage> FetchAsync(
        string url,
        CancellationToken cancellationToken,
        string? hostHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (hostHeader is not null)
        {
            request.Headers.Host = hostHeader;
        }

        return await Send(request, cancellationToken);
    }

    public string StoreEndpoint => Endpoint;

    public ServiceProvider ComposePublishingAt(string publicEndpoint) =>
        new ServiceCollection()

            .AddLogging()
            .AddStorageModule(Configuration(publicEndpoint))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _scope.DisposeAsync();

            await _host.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    private static void Apply(HttpContent content, string name, string value)
    {
        if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);

            return;
        }

        if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        content.Headers.TryAddWithoutValidation(name, value);
    }

    private IConfiguration Configuration(string publicEndpoint = "") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"{FilesStorageS3Options.SectionName}:BucketName"] = _bucketName,

                [$"{FilesStorageS3Options.SectionName}:Endpoint"] = Endpoint,

                [$"{FilesStorageS3Options.SectionName}:PublicEndpoint"] = publicEndpoint,
                [$"{FilesStorageS3Options.SectionName}:Region"] = "us-east-1",

                [$"{FilesStorageS3Options.SectionName}:ForcePathStyle"] = "true",

                [$"{FilesStorageS3Options.SectionName}:AllowInsecureTransport"] = "true",
                [$"{FilesStorageS3Options.SectionName}:AccessKeyId"] = _accessKeyId,
                [$"{FilesStorageS3Options.SectionName}:SecretAccessKey"] = _secretAccessKey,
            })
            .Build();

    private string Endpoint =>
        new UriBuilder(Uri.UriSchemeHttp, _container.Hostname, _container.GetMappedPublicPort(_s3Port)).Uri
            .GetLeftPart(UriPartial.Authority);

    private TService Resolve<TService>()
        where TService : notnull =>
        (_host is null ? throw NotInitialised() : _scope.ServiceProvider).GetRequiredService<TService>();

    private const int _transportAttempts = 3;

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            using var client = Resolve<IHttpClientFactory>().CreateClient();

            try
            {
                using var attempted = await CloneAsync(request, cancellationToken);

                return await client.SendAsync(attempted, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < _transportAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content is { } content)
        {
            byte[] body = await content.ReadAsByteArrayAsync(cancellationToken);
            var copy = new ByteArrayContent(body);

            foreach (var header in content.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = copy;
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private static InvalidOperationException NotInitialised() =>
        new($"{nameof(ObjectStoreFixture)} has not been initialised.");
}

[CollectionDefinition(Name)]
public sealed class ObjectStoreCollectionDefinition : ICollectionFixture<ObjectStoreFixture>
{
    public const string Name = "AppTemplate object store";
}
