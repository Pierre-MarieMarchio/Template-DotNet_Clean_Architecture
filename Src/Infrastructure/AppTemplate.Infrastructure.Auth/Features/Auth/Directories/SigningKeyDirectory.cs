using System.Text.Json;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Directories;

/// <summary>
/// Fetches and caches a provider's JWKS. <b>A typed client, deliberately:</b> the hosts install the
/// one outbound budget on <c>IHttpClientFactory</c>'s defaults from <c>Common/Outbound/</c>, and a
/// client registered with <c>AddHttpClient</c> inherits every part of it — attempt timeout, total
/// timeout, retry on the safe verbs, circuit breaker, concurrency bound — without naming any of it,
/// which is why none of it appears in this file. The escape this type exists to avoid is real and
/// close by: <c>JwtBearerOptions.Backchannel</c> and <c>HttpDocumentRetriever</c>'s parameterless
/// constructor both new up an <c>HttpClient</c> inside the package, which would take this module's
/// outbound calls out of the budget silently — and <c>NoType_ConstructsItsOwnHttpClient</c> could
/// not have seen it, because that rule reads this repository's own source.
/// </summary>
internal sealed class SigningKeyDirectory(
    HttpClient httpClient,
    CachedSigningKeys cache,
    IOptions<ExternalIdentityOptions> options,
    IDateTimeProvider dateTimeProvider,
    ILogger<SigningKeyDirectory> logger) : ISigningKeyDirectory
{
    /// <summary>
    /// The shortest interval between two forced re-fetches of one provider's keys. One minute, and
    /// both directions of the trade are uncomfortable: longer, and a rotation the cache missed
    /// keeps refusing real sign-ins for that long; shorter, and a caller can drive one outbound
    /// request per token by inventing a <c>kid</c>. A minute caps the forged-token cost at sixty
    /// requests an hour per provider and a missed rotation at sixty seconds, well inside the head
    /// start every provider gives by publishing a key before signing with it.
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
        // Broad on purpose: the failures to survive include Polly's own circuit-breaker and timeout
        // exceptions, which this module has no reference to name. A login must not become a 500
        // because a provider is unreachable in a way nobody enumerated.
        catch (Exception exception) when (exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            // Every refusal answers the caller the same way, so this log is the only place the
            // difference between "forged" and "the provider is down" is visible.
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

        // GetSigningKeys drops entries whose "use" is not "sig", and entries this platform cannot
        // build a key from, rather than failing the whole set.
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
