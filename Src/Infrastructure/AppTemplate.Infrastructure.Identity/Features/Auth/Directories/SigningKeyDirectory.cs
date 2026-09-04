using System.Text.Json;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Directories;

/// <summary>
/// Fetches and caches a provider's JWKS.
/// <para>
/// <b>A typed client, deliberately.</b> The hosts install the one outbound budget on
/// <c>IHttpClientFactory</c>'s defaults from <c>Common/Outbound/</c>, and a client registered with
/// <c>AddHttpClient</c> inherits every part of it — attempt timeout, total timeout, retry on the
/// safe verbs, circuit breaker, concurrency bound — without naming any of it. That inheritance is
/// the reason the timeouts and the back-off do not appear in this file, and the circuit breaker is
/// specifically what stops a provider that is down from turning every sign-in into a ten-second
/// wait.
/// </para>
/// <para>
/// The escape this type exists to avoid is real and close by:
/// <c>JwtBearerOptions.Backchannel</c>, left unassigned, is built from an <c>HttpClient</c> the
/// handler news up itself, and the same is true of <c>HttpDocumentRetriever</c>'s parameterless
/// constructor — which is what <c>ConfigurationManager&lt;OpenIdConnectConfiguration&gt;</c> uses
/// unless it is handed one. Either would have taken this module's outbound calls out of the budget
/// silently, and <c>NoType_ConstructsItsOwnHttpClient</c> would not have seen it: that rule reads
/// this repository's own source for the construction, and in both of those cases the construction
/// happens inside the package.
/// </para>
/// </summary>
internal sealed class SigningKeyDirectory(
    HttpClient httpClient,
    CachedSigningKeys cache,
    IOptions<ExternalIdentityOptions> options,
    IDateTimeProvider dateTimeProvider,
    ILogger<SigningKeyDirectory> logger) : ISigningKeyDirectory
{
    /// <summary>
    /// The shortest interval between two forced re-fetches of one provider's keys.
    /// <para>
    /// One minute, and both directions of the trade are uncomfortable. Longer, and a rotation the
    /// cache missed keeps refusing real sign-ins for that long; shorter, and a caller can drive one
    /// outbound request per token by inventing a <c>kid</c>. A minute caps the forged-token cost at
    /// sixty requests an hour per provider and caps a missed rotation at sixty seconds, which is
    /// well inside the head start every provider gives by publishing a key before signing with it.
    /// </para>
    /// </summary>
    private static readonly TimeSpan _minimumTimeBetweenForcedFetches = TimeSpan.FromMinutes(1);

    /// <summary>The member of an OpenID Connect discovery document that names the key set.</summary>
    private const string _jwksUriMember = "jwks_uri";

    public async Task<IReadOnlyCollection<SecurityKey>> GetAsync(
        ExternalIdentityProviderOptions provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var cached = cache.Find(provider.Name);

        return cached is not null && dateTimeProvider.UtcNow - cached.FetchedAt < options.Value.KeySetLifetime
            ? cached.Keys
            : await FetchAsync(provider, cached, cancellationToken);
    }

    public async Task<IReadOnlyCollection<SecurityKey>> RefreshAsync(
        ExternalIdentityProviderOptions provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var cached = cache.Find(provider.Name);

        return cached is not null
            && dateTimeProvider.UtcNow - cached.AttemptedAt < _minimumTimeBetweenForcedFetches
            ? cached.Keys
            : await FetchAsync(provider, cached, cancellationToken);
    }

    /// <summary>
    /// One attempt, and a failure that keeps what was already known.
    /// <para>
    /// A provider being briefly unreachable must not refuse every sign-in: the keys served a few
    /// minutes ago are still the keys, and the alternative — an empty set — refuses tokens that are
    /// perfectly authentic. What is <em>not</em> done is treat the failure as a fetch: the timestamp
    /// the lifetime is measured from stays where it was, so stale keys expire on schedule rather
    /// than being renewed by the provider's absence.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyCollection<SecurityKey>> FetchAsync(
        ExternalIdentityProviderOptions provider,
        SigningKeySet? cached,
        CancellationToken cancellationToken)
    {
        var attemptedAt = dateTimeProvider.UtcNow;

        try
        {
            var keys = await ReadKeySetAsync(provider, cancellationToken);

            cache.Store(provider.Name, new SigningKeySet(keys, attemptedAt, attemptedAt));

            return keys;
        }
        // Broad on purpose, and narrowed only against the caller's own cancellation. The failures
        // this has to survive are not a list anybody could keep correct: an HttpRequestException, a
        // malformed key set, a timeout, and — the ones this project cannot even name — the circuit
        // breaker's and the timeout strategy's own exception types, which live in Polly and are
        // reachable from the host's default policy but from no reference this module has. A login
        // must not become a 500 because a provider is unreachable in a way nobody enumerated.
        catch (Exception exception) when (exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            // Nothing about the token is logged, and nothing about the failure reaches the caller:
            // every refusal answers the same way, so the operator's log is the only place the
            // difference between "forged" and "our provider is down" is visible.
            logger.LogWarning(
                exception,
                "Could not fetch the signing keys of external identity provider '{Provider}'. " +
                "{CachedKeyCount} previously fetched key(s) remain in use.",
                provider.Name,
                cached?.Keys.Count ?? 0);

            cache.Store(
                provider.Name,
                new SigningKeySet(
                    cached?.Keys ?? [],
                    cached?.FetchedAt ?? DateTimeOffset.MinValue,
                    attemptedAt));

            return cached?.Keys ?? [];
        }
    }

    private async Task<IReadOnlyCollection<SecurityKey>> ReadKeySetAsync(
        ExternalIdentityProviderOptions provider,
        CancellationToken cancellationToken)
    {
        string jwksUri = string.IsNullOrWhiteSpace(provider.JwksUri)
            ? await ReadJwksUriFromMetadataAsync(provider.MetadataAddress, cancellationToken)
            : provider.JwksUri;

        string json = await GetStringAsync(jwksUri, cancellationToken);

        // GetSigningKeys is what turns the JSON into usable keys: it drops entries whose "use" is
        // not "sig" and entries this platform cannot build a key from, rather than failing the whole
        // set because one member is a key type nobody here understands.
        return [.. JsonWebKeySet.Create(json).GetSigningKeys()];
    }

    private async Task<string> ReadJwksUriFromMetadataAsync(
        string metadataAddress,
        CancellationToken cancellationToken)
    {
        string json = await GetStringAsync(metadataAddress, cancellationToken);

        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty(_jwksUriMember, out var jwksUri)
            || jwksUri.ValueKind is not JsonValueKind.String
            || jwksUri.GetString() is not { Length: > 0 } address)
        {
            throw new InvalidOperationException(
                $"The discovery document at '{metadataAddress}' declares no '{_jwksUriMember}'.");
        }

        return address;
    }

    private async Task<string> GetStringAsync(string uri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(new Uri(uri), cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
