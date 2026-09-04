using System.Net;
using System.Security.Cryptography;
using AppTemplate.Infrastructure.Identity.Features.Auth.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using AppTemplate.Infrastructure.Identity.UnitTests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Features.Auth.Directories;

/// <summary>
/// The cache in front of a provider's JWKS, and what it does when the provider is not there.
/// <para>
/// The clock is the repository's injectable one, which does control this — unlike JWT validation and
/// ASP.NET Identity, both of which read <c>TimeProvider.System</c>. The cache lifetime is this
/// module's own arithmetic, so moving the clock is the honest way to test it.
/// </para>
/// <para>
/// The <c>HttpClient</c> is built here over a counting stub. That is the one place in this
/// repository where constructing one is right: the production rule
/// (<c>NoType_ConstructsItsOwnHttpClient</c>) is about <c>Src/</c>, where a client built by hand
/// escapes the outbound budget the hosts install — and what is under test here is precisely that
/// this type takes an <c>HttpClient</c> rather than making one.
/// </para>
/// </summary>
public sealed class SigningKeyDirectoryTests : IDisposable
{
    private const string _providerName = "google";
    private const string _jwksUri = "https://www.googleapis.com/oauth2/v3/certs";
    private const string _metadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

    private readonly RSA _key = RSA.Create(2048);
    private readonly RSA _rotatedKey = RSA.Create(2048);
    private readonly CountingKeySetEndpoint _endpoint = new();
    private readonly MovableDateTimeProvider _clock = new();
    private readonly CachedSigningKeys _cache = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _key.Dispose();
        _rotatedKey.Dispose();
        _endpoint.Dispose();
    }

    [Fact]
    public async Task GetAsync_FetchesTheKeySetAndReturnsItsSigningKeys()
    {
        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        var keys = await CreateDirectory().GetAsync(Provider(), TestToken);

        keys.ShouldHaveSingleItem().KeyId.ShouldBe("current");
        _endpoint.RequestCount.ShouldBe(1);
    }

    /// <summary>
    /// The reason the cache exists: without it the provider would be on the hot path of every
    /// single sign-in, and its availability would become this API's availability.
    /// </summary>
    [Fact]
    public async Task GetAsync_ServesASecondCallFromMemory()
    {
        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        var directory = CreateDirectory();
        await directory.GetAsync(Provider(), TestToken);
        await directory.GetAsync(Provider(), TestToken);

        _endpoint.RequestCount.ShouldBe(1);
    }

    /// <summary>
    /// The other half: a cache that never lapsed would keep trusting a key the provider withdrew for
    /// as long as the process lived.
    /// </summary>
    [Fact]
    public async Task GetAsync_FetchesAgainOnceTheCachedSetHasLapsed()
    {
        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        var options = Options();
        var directory = CreateDirectory(options);
        await directory.GetAsync(Provider(), TestToken);

        _clock.Advance(options.Value.KeySetLifetime + TimeSpan.FromSeconds(1));
        await directory.GetAsync(Provider(), TestToken);

        _endpoint.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetAsync_ReadsTheKeySetAddressFromADiscoveryDocument()
    {
        _endpoint.Answer(_metadataAddress, $$"""{"issuer":"https://accounts.google.com","jwks_uri":"{{_jwksUri}}"}""");
        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        var keys = await CreateDirectory().GetAsync(ProviderByDiscovery(), TestToken);

        keys.ShouldHaveSingleItem().KeyId.ShouldBe("current");
        _endpoint.RequestCount.ShouldBe(2);
    }

    /// <summary>
    /// A provider that is briefly unreachable must not refuse every sign-in. The keys it served a
    /// few minutes ago are still its keys, and an empty set would refuse tokens that are perfectly
    /// authentic.
    /// </summary>
    [Fact]
    public async Task GetAsync_KeepsUsingTheLastKeySetWhenTheProviderStopsAnswering()
    {
        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        var options = Options();
        var directory = CreateDirectory(options);
        await directory.GetAsync(Provider(), TestToken);

        _endpoint.Fail(_jwksUri, HttpStatusCode.ServiceUnavailable);
        _clock.Advance(options.Value.KeySetLifetime + TimeSpan.FromSeconds(1));

        var keys = await directory.GetAsync(Provider(), TestToken);

        keys.ShouldHaveSingleItem().KeyId.ShouldBe("current");
    }

    /// <summary>
    /// A failure is not a fetch. If it renewed the lifetime, a provider that went down would
    /// silently extend the life of the keys it served before it did.
    /// </summary>
    [Fact]
    public async Task GetAsync_DoesNotTreatAFailedFetchAsAFreshOne()
    {
        _endpoint.Fail(_jwksUri, HttpStatusCode.ServiceUnavailable);

        var directory = CreateDirectory();
        await directory.GetAsync(Provider(), TestToken);

        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        // No time has passed, so a lifetime measured from the failed attempt would answer from an
        // empty cache instead of asking again.
        var keys = await directory.GetAsync(Provider(), TestToken);

        keys.ShouldHaveSingleItem().KeyId.ShouldBe("current");
    }

    /// <summary>
    /// Nothing was ever fetched and nothing is invented. An empty set refuses every token, which is
    /// the only safe answer when the thing that decides authenticity is unavailable.
    /// </summary>
    [Fact]
    public async Task GetAsync_ReturnsNothingWhenTheProviderHasNeverAnswered()
    {
        _endpoint.Fail(_jwksUri, HttpStatusCode.InternalServerError);

        (await CreateDirectory().GetAsync(Provider(), TestToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_ReturnsNothingWhenTheProviderAnswersWithSomethingThatIsNotAKeySet()
    {
        _endpoint.Answer(_jwksUri, "<html>we have moved</html>");

        (await CreateDirectory().GetAsync(Provider(), TestToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_ReturnsNothingWhenTheDiscoveryDocumentNamesNoKeySet()
    {
        _endpoint.Answer(_metadataAddress, """{"issuer":"https://accounts.google.com"}""");

        (await CreateDirectory().GetAsync(ProviderByDiscovery(), TestToken)).ShouldBeEmpty();
    }

    /// <summary>
    /// What a rotation looks like from here: the cached set is well inside its lifetime, so nothing
    /// but a token naming a key it does not hold would ever ask again. A refresh ignores the
    /// lifetime — and only the lifetime; the floor below still applies.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_FetchesAgainEvenThoughTheCachedSetIsStillFresh()
    {
        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        var options = Options();
        var directory = CreateDirectory(options);
        await directory.GetAsync(Provider(), TestToken);

        _endpoint.Answer(_jwksUri, KeySet(_rotatedKey, "rotated"));
        _clock.Advance(TimeSpan.FromMinutes(2));
        options.Value.KeySetLifetime.ShouldBeGreaterThan(
            TimeSpan.FromMinutes(2),
            "The point of this test is that the cached set has not lapsed, so the elapsed time has " +
            "to stay well inside the lifetime while clearing the forced-refresh floor.");

        var keys = await directory.RefreshAsync(Provider(), TestToken);

        keys.ShouldHaveSingleItem().KeyId.ShouldBe("rotated");
        _endpoint.RequestCount.ShouldBe(2);
    }

    /// <summary>
    /// The floor, and the reason it exists: a caller that invents a key identifier would otherwise
    /// turn one forged token into one outbound request, and a flood of them into a flood of
    /// requests aimed at the provider by this API.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_RefusesToFetchAgainImmediately()
    {
        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        var directory = CreateDirectory();
        await directory.RefreshAsync(Provider(), TestToken);
        await directory.RefreshAsync(Provider(), TestToken);
        await directory.RefreshAsync(Provider(), TestToken);

        _endpoint.RequestCount.ShouldBe(1);
    }

    [Fact]
    public async Task RefreshAsync_FetchesAgainOnceTheFloorHasPassed()
    {
        _endpoint.Answer(_jwksUri, KeySet(_key, "current"));

        var directory = CreateDirectory();
        await directory.RefreshAsync(Provider(), TestToken);

        _clock.Advance(TimeSpan.FromMinutes(2));
        await directory.RefreshAsync(Provider(), TestToken);

        _endpoint.RequestCount.ShouldBe(2);
    }

    /// <summary>
    /// The floor counts attempts, not successes. Counting only successes would make a provider that
    /// is down the cheapest way to have this API hammer it.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_CountsAFailedAttemptAgainstTheFloor()
    {
        _endpoint.Fail(_jwksUri, HttpStatusCode.ServiceUnavailable);

        var directory = CreateDirectory();
        await directory.RefreshAsync(Provider(), TestToken);
        await directory.RefreshAsync(Provider(), TestToken);

        _endpoint.RequestCount.ShouldBe(1);
    }

    private SigningKeyDirectory CreateDirectory(IOptions<ExternalIdentityOptions>? options = null) =>
        new(
            _endpoint.CreateClient(),
            _cache,
            options ?? Options(),
            _clock,
            NullLogger<SigningKeyDirectory>.Instance);

    private static OptionsWrapper<ExternalIdentityOptions> Options() => new(new ExternalIdentityOptions());

    private static ExternalIdentityProviderOptions Provider() => Provider(_jwksUri, string.Empty);

    private static ExternalIdentityProviderOptions ProviderByDiscovery() =>
        Provider(string.Empty, _metadataAddress);

    private static ExternalIdentityProviderOptions Provider(string jwksUri, string metadataAddress)
    {
        var provider = new ExternalIdentityProviderOptions
        {
            Name = _providerName,
            JwksUri = jwksUri,
            MetadataAddress = metadataAddress,
        };

        provider.Issuers.Add("https://accounts.google.com");
        provider.Audiences.Add("1234.apps.googleusercontent.com");

        return provider;
    }

    /// <summary>A JWKS document carrying the public half of one RSA key, as a provider publishes it.</summary>
    private static string KeySet(RSA key, string keyId)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);

        string modulus = Base64UrlEncoder.Encode(parameters.Modulus);
        string exponent = Base64UrlEncoder.Encode(parameters.Exponent);

        return $$"""
            {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{keyId}}","n":"{{modulus}}","e":"{{exponent}}"}]}
            """;
    }
}

/// <summary>
/// A stub the directory's <c>HttpClient</c> talks to, counting every request so a cache hit is
/// distinguishable from a fetch that happened to return the same bytes.
/// </summary>
internal sealed class CountingKeySetEndpoint : IDisposable
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _answers = new(StringComparer.Ordinal);
    private readonly List<HttpClient> _clients = [];

    internal int RequestCount { get; private set; }

    internal void Answer(string uri, string body) =>
        _answers[uri] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };

    internal void Fail(string uri, HttpStatusCode status) =>
        _answers[uri] = () => new HttpResponseMessage(status);

    internal HttpClient CreateClient()
    {
        var client = new HttpClient(new StubHandler(this), disposeHandler: true);
        _clients.Add(client);

        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    private HttpResponseMessage Send(Uri uri)
    {
        RequestCount++;

        return _answers.TryGetValue(uri.ToString(), out var answer)
            ? answer()
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private sealed class StubHandler(CountingKeySetEndpoint endpoint) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(endpoint.Send(request.RequestUri!));
    }
}
