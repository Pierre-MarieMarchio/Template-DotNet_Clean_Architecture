using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;
using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Directories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Adapters.Services;

internal sealed class ExternalIdentityVerifier(
    IOptions<ExternalIdentityOptions> options,
    ISigningKeyDirectory signingKeys) : IExternalIdentityVerifier
{
    private static readonly JsonWebTokenHandler _handler = new();

    private static readonly string[] _permittedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.EcdsaSha256,
    ];

    private static readonly TimeSpan _clockSkew = TimeSpan.FromSeconds(30);

    public async Task<VerifiedExternalIdentity?> VerifyAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configured = options.Value.Find(provider);

        if (configured is null)
        {
            return null;
        }

        var keys = await signingKeys.GetAsync(configured, cancellationToken);
        var validation = await ValidateAsync(idToken, configured, keys);

        if (validation is { IsValid: false, Exception: SecurityTokenSignatureKeyNotFoundException })
        {
            keys = await signingKeys.RefreshAsync(configured, cancellationToken);
            validation = await ValidateAsync(idToken, configured, keys);
        }

        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken token)
        {
            return null;
        }

        if (!token.TryGetPayloadValue(JwtRegisteredClaimNames.Sub, out string? subject)
            || string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        return new VerifiedExternalIdentity(
            configured.Name,
            subject,
            ReadEmail(token),
            ReadEmailVerified(token));
    }

    private static Task<TokenValidationResult> ValidateAsync(
        string idToken,
        ExternalIdentityProviderOptions provider,
        IReadOnlyCollection<SecurityKey> keys) =>
        _handler.ValidateTokenAsync(
            idToken,
            new TokenValidationParameters
            {
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateIssuerSigningKey = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,

                ValidIssuers = provider.Issuers,
                ValidAudiences = provider.Audiences,
                IssuerSigningKeys = keys,
                ValidAlgorithms = _permittedAlgorithms,
                ClockSkew = _clockSkew,
            });

    private static string? ReadEmail(JsonWebToken token) =>
        token.TryGetPayloadValue(JwtRegisteredClaimNames.Email, out string? email)
            && !string.IsNullOrWhiteSpace(email)
                ? email
                : null;

    private static bool ReadEmailVerified(JsonWebToken token) =>
        token.TryGetPayloadValue(JwtRegisteredClaimNames.EmailVerified, out bool verified) && verified;
}
