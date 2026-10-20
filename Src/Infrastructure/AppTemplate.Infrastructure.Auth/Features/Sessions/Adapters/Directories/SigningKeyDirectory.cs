using System.Text.Json;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Directories;

internal sealed class SigningKeyDirectory(
    HttpClient httpClient,
    CachedSigningKeys cache,
    IOptions<ExternalIdentityOptions> options,
    IDateTimeProvider dateTimeProvider,
    ILogger<SigningKeyDirectory> logger) : ISigningKeyDirectory
{
    private static readonly TimeSpan _minimumTimeBetweenForcedFetches = TimeSpan.FromMinutes(1);

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
        catch (Exception exception) when (exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
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
