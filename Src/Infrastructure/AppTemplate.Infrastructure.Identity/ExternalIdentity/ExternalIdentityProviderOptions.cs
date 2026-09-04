namespace AppTemplate.Infrastructure.Identity.ExternalIdentity;

/// <summary>
/// One identity provider this installation accepts an <c>id_token</c> from.
/// <para>
/// <b>There is no secret here, and there is no room for one.</b> Verifying an <c>id_token</c> is a
/// signature check against a public key set, so a client secret would buy nothing and would only
/// create somewhere for one to leak. Anything asking for a client secret is the authorization-code
/// exchange, which happens on the client in this template — see <c>IExternalIdentityVerifier</c>.
/// </para>
/// <para>
/// Every field but the key-set address is a <em>value</em>, which is why Google, Microsoft and Apple
/// are three configuration sections rather than three classes: OpenID Connect makes the check
/// identical and only the strings differ.
/// </para>
/// </summary>
public sealed class ExternalIdentityProviderOptions
{
    /// <summary>
    /// The name a client passes as the provider — <c>google</c>, <c>microsoft</c>, <c>apple</c>. It
    /// is matched case-insensitively and is the only thing tying a request to this section.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The <c>iss</c> values a token from this provider may carry. A list because one provider can
    /// legitimately mint more than one form: Google's ID tokens carry either
    /// <c>https://accounts.google.com</c> or the bare <c>accounts.google.com</c>, and an
    /// installation that configured only one of them would refuse a share of real sign-ins.
    /// <para>
    /// Each entry is compared whole. It is not a pattern, so a provider whose issuer embeds a tenant
    /// identifier needs the tenant-specific authority configured here rather than the multi-tenant
    /// one — see <c>docs/CONFIGURATION.md</c>.
    /// </para>
    /// </summary>
    public IList<string> Issuers { get; } = [];

    /// <summary>
    /// The <c>aud</c> values a token from this provider may carry: the client identifiers this
    /// installation issued. A list because one product commonly registers several — a web client, an
    /// iOS client and an Android client all sign into the same API — and a token minted for a client
    /// identifier nobody here recognises is a token minted for somebody else's application.
    /// </summary>
    public IList<string> Audiences { get; } = [];

    /// <summary>
    /// The provider's JWKS endpoint, when it is known and stable —
    /// <c>https://www.googleapis.com/oauth2/v3/certs</c>, <c>https://appleid.apple.com/auth/keys</c>.
    /// Mutually exclusive with <see cref="MetadataAddress"/>, and one of the two is required.
    /// </summary>
    public string JwksUri { get; set; } = string.Empty;

    /// <summary>
    /// The provider's OpenID Connect discovery document, from which <c>jwks_uri</c> is read. One
    /// extra request per key-set fetch, in exchange for a provider that may move its key set.
    /// </summary>
    public string MetadataAddress { get; set; } = string.Empty;
}
