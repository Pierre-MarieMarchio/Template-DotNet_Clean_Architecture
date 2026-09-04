using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.ExternalIdentity;

/// <summary>
/// Which identity providers this installation accepts an <c>id_token</c> from, and how long their
/// signing keys are believed.
/// <para>
/// <b>An empty section is valid and is the default.</b> A deployment that does not offer external
/// sign-in boots normally and refuses every attempt as
/// <c>ExternalIdentityStatus.UnknownProvider</c>; a validator that demanded a provider would stop
/// the process for a feature the project may never turn on.
/// </para>
/// </summary>
public sealed class ExternalIdentityOptions
{
    public const string SectionName = "ExternalIdentity";

    /// <summary>The shortest and longest key-set lifetime the validator will accept.</summary>
    internal static readonly TimeSpan MinimumKeySetLifetime = TimeSpan.FromMinutes(1);

    internal static readonly TimeSpan MaximumKeySetLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// The providers, in no particular order. Read-only by design: the binder fills it, and a
    /// setter would let a second configuration source replace the list rather than add to it.
    /// </summary>
    public IList<ExternalIdentityProviderOptions> Providers { get; } = [];

    /// <summary>
    /// How long a fetched key set is used before it is fetched again.
    /// <para>
    /// Fifteen minutes, and the number is a compromise between two failures rather than a guess.
    /// Fetching per sign-in would put the provider on the hot path of every login; a long-lived
    /// cache means a key the provider <em>withdrew</em> stays trusted here for as long as the cache
    /// says. Fifteen minutes bounds that window at fifteen minutes and costs ninety-six requests a
    /// day per provider, which is nothing.
    /// </para>
    /// <para>
    /// Key <em>rotation</em> — the far more common event — is not handled by this number at all: a
    /// token naming a key the cache does not hold triggers an immediate re-fetch, so a new key is
    /// picked up on the first token that uses it rather than up to fifteen minutes later. See
    /// <see cref="SigningKeyDirectory"/>.
    /// </para>
    /// </summary>
    public TimeSpan KeySetLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The provider configured under <paramref name="name"/>, or <c>null</c>. Case-insensitive: the
    /// name arrives from a client, and refusing <c>Google</c> because the section says <c>google</c>
    /// would be an outage nobody could read from the response.
    /// </summary>
    internal ExternalIdentityProviderOptions? Find(string name)
    {
        foreach (var provider in Providers)
        {
            if (string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return provider;
            }
        }

        return null;
    }
}

/// <summary>
/// Refuses a provider entry that could only fail later, at the one moment nobody is watching: the
/// first time a user signs in through it.
/// </summary>
internal sealed class ExternalIdentityOptionsValidator : IValidateOptions<ExternalIdentityOptions>
{
    public ValidateOptionsResult Validate(string? name, ExternalIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.KeySetLifetime < ExternalIdentityOptions.MinimumKeySetLifetime
            || options.KeySetLifetime > ExternalIdentityOptions.MaximumKeySetLifetime)
        {
            failures.Add(
                $"'{ExternalIdentityOptions.SectionName}:KeySetLifetime' must be between " +
                $"{ExternalIdentityOptions.MinimumKeySetLifetime} and " +
                $"{ExternalIdentityOptions.MaximumKeySetLifetime}.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < options.Providers.Count; index++)
        {
            Validate(options.Providers[index], index, seen, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Validate(
        ExternalIdentityProviderOptions provider,
        int index,
        HashSet<string> seen,
        List<string> failures)
    {
        string at = $"'{ExternalIdentityOptions.SectionName}:Providers:{index}";

        if (string.IsNullOrWhiteSpace(provider.Name))
        {
            failures.Add($"{at}:Name' is required; it is what a client asks for.");
        }
        else if (!seen.Add(provider.Name))
        {
            // Two entries under one name means one of them is never reached, and which one is an
            // accident of ordering.
            failures.Add($"{at}:Name' repeats '{provider.Name}', which is already configured.");
        }

        if (provider.Issuers.Count == 0 || provider.Issuers.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add(
                $"{at}:Issuers' must list at least one non-empty issuer; issuer validation is never " +
                "disabled.");
        }

        if (provider.Audiences.Count == 0 || provider.Audiences.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add(
                $"{at}:Audiences' must list at least one non-empty client identifier; audience " +
                "validation is never disabled.");
        }

        ValidateKeySetAddress(provider, at, failures);
    }

    /// <summary>
    /// Exactly one address, and it must be absolute and HTTPS. Plaintext would put the key set — the
    /// only thing deciding whether a token is authentic — in reach of whoever is between the two
    /// hosts.
    /// </summary>
    private static void ValidateKeySetAddress(
        ExternalIdentityProviderOptions provider,
        string at,
        List<string> failures)
    {
        bool hasJwks = !string.IsNullOrWhiteSpace(provider.JwksUri);
        bool hasMetadata = !string.IsNullOrWhiteSpace(provider.MetadataAddress);

        if (hasJwks == hasMetadata)
        {
            failures.Add(
                $"{at}' must set exactly one of 'JwksUri' and 'MetadataAddress'" +
                (hasJwks ? ", not both." : "."));

            return;
        }

        string address = hasJwks ? provider.JwksUri : provider.MetadataAddress;
        string key = hasJwks ? "JwksUri" : "MetadataAddress";

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            failures.Add($"{at}:{key}' must be an absolute https URL.");
        }
    }
}
