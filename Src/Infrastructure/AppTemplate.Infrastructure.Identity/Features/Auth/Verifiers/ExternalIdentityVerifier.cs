using AppTemplate.Application.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Infrastructure.Identity.Features.Auth.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Verifiers;

/// <summary>
/// <see cref="IExternalIdentityVerifier"/> over OpenID Connect, for every provider at once.
/// <para>
/// <b>One adapter, not one per provider.</b> Google, Microsoft and Apple all mint an OpenID Connect
/// <c>id_token</c>, and checking one is the same four checks in all three cases: the signature
/// against the provider's published keys, the issuer, the audience, and the validity window. What
/// differs between them is a set of strings — an issuer, a client identifier, a key-set address —
/// so three classes would be three copies of this one with three different constants, and adding a
/// fourth provider would be a class rather than a configuration section.
/// </para>
/// <para>
/// The one provider-shaped difference that is real lives at the bottom of this file, and it is a
/// difference in a <em>value's type</em> rather than in behaviour: Apple sends
/// <c>email_verified</c> as the string <c>"true"</c> where Google sends the JSON boolean.
/// </para>
/// </summary>
internal sealed class ExternalIdentityVerifier(
    IOptions<ExternalIdentityOptions> options,
    ISigningKeyDirectory signingKeys) : IExternalIdentityVerifier
{
    /// <summary>
    /// Thread-safe and stateless once constructed, and building one per verification is measurable
    /// work for no benefit.
    /// </summary>
    private static readonly JsonWebTokenHandler _handler = new();

    /// <summary>
    /// Pinned, so a token cannot dictate how it is checked. The two asymmetric algorithms every
    /// OpenID Connect provider in practice signs with; leaving this open is how a token signed with
    /// <c>HS256</c> — using the provider's <em>public</em> key as the shared secret, which is
    /// published — gets accepted as authentic.
    /// </summary>
    private static readonly string[] _permittedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.EcdsaSha256,
    ];

    /// <summary>
    /// Small, not zero, and for a stronger reason than the one on
    /// <c>ConfigureJwtBearerOptions</c>: these timestamps are stamped by somebody else's clock
    /// entirely, on a host this deployment has no relationship with. Far below the framework's
    /// five-minute default, which keeps a token usable well past the moment its issuer said it
    /// stopped being.
    /// </summary>
    private static readonly TimeSpan _clockSkew = TimeSpan.FromSeconds(30);

    public async Task<ExternalIdentityOutcome> VerifyAsync(
        string provider,
        string idToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configured = options.Value.Find(provider);

        if (configured is null)
        {
            return ExternalIdentityOutcome.Refused(ExternalIdentityStatus.UnknownProvider);
        }

        var keys = await signingKeys.GetAsync(configured, cancellationToken);
        var validation = await ValidateAsync(idToken, configured, keys);

        // The token names a key the cached set does not hold, which is what a rotation looks like
        // from here — and is not the same event as a bad signature, which no re-fetch would fix. The
        // directory decides whether the request is actually made; this only asks.
        if (validation is { IsValid: false, Exception: SecurityTokenSignatureKeyNotFoundException })
        {
            keys = await signingKeys.RefreshAsync(configured, cancellationToken);
            validation = await ValidateAsync(idToken, configured, keys);
        }

        // Everything after this point reads attacker-supplied text unless IsValid holds.
        if (!validation.IsValid || validation.SecurityToken is not JsonWebToken token)
        {
            return ExternalIdentityOutcome.Refused(ExternalIdentityStatus.InvalidToken);
        }

        if (!token.TryGetPayloadValue(JwtRegisteredClaimNames.Sub, out string? subject)
            || string.IsNullOrWhiteSpace(subject))
        {
            // A token with no subject verifies but identifies nobody, and the pair (provider,
            // subject) is the only key a local account is ever resolved by.
            return ExternalIdentityOutcome.Refused(ExternalIdentityStatus.InvalidToken);
        }

        return ExternalIdentityOutcome.Verified(
            new VerifiedExternalIdentity(
                configured.Name,
                subject,
                ReadEmail(token),
                ReadEmailVerified(token)));
    }

    private static Task<TokenValidationResult> ValidateAsync(
        string idToken,
        ExternalIdentityProviderOptions provider,
        IReadOnlyCollection<SecurityKey> keys) =>
        _handler.ValidateTokenAsync(
            idToken,
            new TokenValidationParameters
            {
                // None of these is conditional on a value being present. A check that switches
                // itself off when its configuration is blank is the defect JwtOptionsValidator
                // exists to have prevented once already; here the options validator guarantees the
                // values and these stay true unconditionally.
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

    /// <summary><c>null</c> whenever the token carried no address, which is Apple's normal case.</summary>
    private static string? ReadEmail(JsonWebToken token) =>
        token.TryGetPayloadValue(JwtRegisteredClaimNames.Email, out string? email)
            && !string.IsNullOrWhiteSpace(email)
                ? email
                : null;

    /// <summary>
    /// <c>email_verified</c>. Google sends the JSON boolean and Apple sends the string
    /// <c>"true"</c>, and reading only one of the two would silently treat every Apple address as
    /// unverified — which refuses every first Apple sign-in with an error saying nothing about why.
    /// <para>
    /// One call covers both, and that is a fact about the library rather than an assumption:
    /// <c>TryGetPayloadValue&lt;bool&gt;</c> converts the string form. It is asserted rather than
    /// trusted — <c>ExternalIdentityVerifierTests</c> presents Apple's encoding, so a version that
    /// stopped converting fails there instead of in production.
    /// </para>
    /// <para>
    /// Absent means <c>false</c>. An omission is never read as an assurance.
    /// </para>
    /// </summary>
    private static bool ReadEmailVerified(JsonWebToken token) =>
        token.TryGetPayloadValue(JwtRegisteredClaimNames.EmailVerified, out bool verified) && verified;
}
