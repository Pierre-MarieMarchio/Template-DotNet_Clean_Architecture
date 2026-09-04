using System.Globalization;
using System.Net.Http.Headers;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage;
using AppTemplate.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Minio;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

/// <summary>
/// A real S3-compatible object store, and the storage module composed against it exactly as a host
/// composes it.
/// </summary>
/// <remarks>
/// <para>
/// The adapters are reached through <see cref="IFileContentStore"/> and
/// <see cref="IFileContentInventory"/> as <see cref="StorageModule.AddStorageModule"/> registers
/// them. They are <c>internal</c> and the module shares its internals with
/// <c>AppTemplate.Infrastructure.Storage.UnitTests</c> alone, which is the right way round: what a
/// use case depends on is the port, and a test naming the class could compose it with a client no
/// host would ever build — which is precisely the mistake this suite exists to rule out.
/// </para>
/// <para>
/// <b>Presigning is arithmetic, and that is why the unit tests can assert the shape of a grant with
/// no store at all.</b> Everything past the signature is a claim about a real store: that a URL this
/// module signs is one MinIO accepts, that the headers named in the grant are the headers the
/// signature covers, that a listing resumes where the token said. Nothing short of a store can
/// answer those, and a stub would only agree with whatever this repository wrote it to say.
/// </para>
/// </remarks>
public sealed class ObjectStoreFixture : IAsyncLifetime
{
    /// <summary>
    /// The same image the development stack runs (<c>docker-compose.yml</c>), pinned to the same
    /// release rather than to the module's own default — which is two years older. What these tests
    /// exercise is then the store a developer already has in front of them.
    /// </summary>
    private const string _minioImage = "minio/minio:RELEASE.2025-04-22T22-12-26Z";

    /// <summary>
    /// Long enough for MinIO, which refuses to start on a root password under eight characters.
    /// </summary>
    private const string _credential = "apptemplate_tests";

    private const string _bucketName = "apptemplate-storage-tests";

    private readonly MinioContainer _container = new MinioBuilder(_minioImage)
        .WithUsername(_credential)
        .WithPassword(_credential)
        .Build();

    private ServiceProvider? _host;
    private AsyncServiceScope _scope;

    /// <summary>The store as a use case sees it.</summary>
    public IFileContentStore Content => Resolve<IFileContentStore>();

    public IFileContentInventory Inventory => Resolve<IFileContentInventory>();

    public async ValueTask InitializeAsync()
    {
        var token = TestContext.Current.CancellationToken;

        await _container.StartAsync(token);

        var services = new ServiceCollection();

        // The suite's own HTTP client, and the reason it comes from a factory rather than from
        // `new HttpClient()`: NoType_ConstructsItsOwnHttpClient forbids that under Src/, and a test
        // suite that did it anyway would be publishing the counter-example next to the rule.
        services.AddHttpClient();
        services.AddStorageModule(Configuration());

        // Scope validation on, because both adapters are registered scoped and resolving one from
        // the root provider would be a composition no host performs.
        _host = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        _scope = _host.CreateAsyncScope();

        // The bucket has to exist before any of it means anything: a signed URL against a missing
        // bucket is refused for a reason that has nothing to do with the signature, which would make
        // every failure in this suite say the same uninformative thing. Created through the module's
        // own client, so a configuration these tests got wrong fails here rather than inside a test.
        await _host.GetRequiredService<IAmazonS3>()
            .PutBucketAsync(new PutBucketRequest { BucketName = _bucketName }, token);
    }

    /// <summary>
    /// A key no other test has used, under a prefix that names the test asking for it.
    /// </summary>
    /// <remarks>
    /// One bucket is shared by every class here, so a fixed key would let one test's leftovers decide
    /// another's outcome — and the inventory tests walk by prefix, so a shared prefix would make one
    /// class's objects turn up in another class's page.
    /// </remarks>
    public static string KeyUnder(string prefix) =>
        $"{prefix}/{Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A prefix no other test walks. The suffix, not the name, is what makes it unique.
    /// </summary>
    public static string UniquePrefix(string purpose) =>
        $"{purpose}-{Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[..12]}";

    /// <summary>
    /// Deposits <paramref name="payload"/> against <paramref name="grant"/>, the way a client holding
    /// one has to: the URL it names, the method it names, and every header it lists sent back
    /// verbatim.
    /// </summary>
    /// <remarks>
    /// <b>Every header the grant names, and no others.</b> The grant carries the digest itself, so a
    /// deposit has nothing of its own to add — and a header sent a second time is refused, since the
    /// signature covers it.
    /// </remarks>
    /// <param name="hostHeader">
    /// As on <see cref="FetchAsync"/>: the name to present the deposit under when it is not the one
    /// the URL names.
    /// </param>
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

    /// <summary>
    /// Follows a signed URL, as the client the API redirects does.
    /// </summary>
    /// <param name="hostHeader">
    /// The name to present the request under, when it is not the one in <paramref name="url"/>. The
    /// connection still goes to the address the URL names — only the <c>Host</c> header changes,
    /// which is the one input a Signature Version 4 URL covers and no DNS is involved in. That is
    /// what lets <c>PublicEndpointTests</c> ask what a store does when the name a URL was signed for
    /// and the name it arrives under disagree, on any machine, with one container.
    /// </param>
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

    /// <summary>The address this machine reaches the store at, and what every grant is signed for.</summary>
    public string StoreEndpoint => Endpoint;

    /// <summary>
    /// The storage module composed a second time, signing for <paramref name="publicEndpoint"/> while
    /// still talking to the same container. The caller owns the provider and disposes it.
    /// </summary>
    /// <remarks>
    /// A second composition rather than a second container: what is under test is the name a
    /// signature is computed for, and one store answering under two names is exactly the deployment
    /// <c>Storage:PublicEndpoint</c> exists for.
    /// </remarks>
    public ServiceProvider ComposePublishingAt(string publicEndpoint) =>
        new ServiceCollection()

            // The content inspector takes a logger, and this composition is the module whole rather
            // than the two adapters the tests reach for.
            .AddLogging()
            .AddStorageModule(Configuration(publicEndpoint))
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();

        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        // CancellationToken.None deliberately: teardown must run even when the test run is being
        // cancelled, or the container is left behind.
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Puts a header where HTTP puts it. <c>Content-Type</c> and <c>Content-Length</c> are entity
    /// headers and .NET refuses them on the request; everything else the signature covers is a
    /// request header. Which side of that line a header falls on changes nothing on the wire, which
    /// is what the signature is computed over.
    /// </summary>
    private static void Apply(HttpContent content, string name, string value)
    {
        if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);

            return;
        }

        // Content-Length is left to the content itself, which knows the real length. Copying the
        // signed one over it would make a deposit of the wrong size claim the right one, and the
        // store would answer about a body that never arrived rather than about the mismatch.
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
                [$"{StorageOptions.SectionName}:BucketName"] = _bucketName,

                // The published port on this machine, which is also the endpoint every URL is signed
                // for — see the class remarks on why the two being the same is a property of the test
                // host rather than of the template.
                [$"{StorageOptions.SectionName}:Endpoint"] = Endpoint,

                // Empty for every suite but PublicEndpointTests, which is the point: the default has
                // to be indistinguishable from the setting not existing.
                [$"{StorageOptions.SectionName}:PublicEndpoint"] = publicEndpoint,
                [$"{StorageOptions.SectionName}:Region"] = "us-east-1",

                // Required against any store reached by host and port: virtual-host addressing would
                // resolve <bucket>.127.0.0.1, which nothing serves.
                [$"{StorageOptions.SectionName}:ForcePathStyle"] = "true",

                // Plain HTTP, and stated rather than inferred: Testcontainers usually maps the port
                // onto loopback, where the validator allows http without asking, but a remote
                // DOCKER_HOST publishes it somewhere else and the suite would then fail on
                // configuration validation instead of on anything it means to assert.
                [$"{StorageOptions.SectionName}:AllowInsecureTransport"] = "true",
                [$"{StorageOptions.SectionName}:AccessKeyId"] = _credential,
                [$"{StorageOptions.SectionName}:SecretAccessKey"] = _credential,
            })
            .Build();

    private string Endpoint => _container.GetConnectionString();

    private TService Resolve<TService>()
        where TService : notnull =>
        (_host is null ? throw NotInitialised() : _scope.ServiceProvider).GetRequiredService<TService>();

    /// <summary>How many times a request is re-sent when it never got an answer at all.</summary>
    /// <remarks>
    /// The hosts install a timeout and a retry budget on <c>IHttpClientFactory</c>'s defaults; this
    /// fixture composes only the storage module, so its client has neither. Every assertion in this
    /// suite is about a status code the store returned, and a transport failure is the one outcome
    /// that is not an answer — so re-sending on that, and only on that, cannot mask anything the
    /// tests are about.
    /// <para>
    /// Written after a full-solution run failed twice on two different tests here, each asserting an
    /// exact status against a container that is starting at the same time as a PostgreSQL one.
    /// Neither was reproducible in twenty-odd runs, so this is the cause the evidence points at
    /// rather than one that was observed. A status — any status — is passed straight through.
    /// </para>
    /// </remarks>
    private const int _transportAttempts = 3;

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            // A new client per call, and cheap: the factory pools the handler underneath, so this
            // costs an object rather than a connection pool. Awaited inside the using, not returned
            // from it — disposing the client while the request is still in flight cancels it.
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

    /// <summary>
    /// A fresh message carrying the same request, because <see cref="HttpRequestMessage"/> cannot be
    /// sent twice and the body has already been consumed by the attempt that failed.
    /// </summary>
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

/// <summary>
/// The one collection every class in <c>Storage/</c> joins.
/// </summary>
/// <remarks>
/// One collection means one MinIO container for the whole suite. Eleven test projects already run
/// together here with a PostgreSQL among them, and a container per class would have added four more
/// for guarantees that share a store perfectly well: every test addresses keys under a prefix of its
/// own, so serialising the classes is not what keeps them apart.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ObjectStoreCollectionDefinition : ICollectionFixture<ObjectStoreFixture>
{
    public const string Name = "AppTemplate object store";
}
