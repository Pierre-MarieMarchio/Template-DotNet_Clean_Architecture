using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;

/// <summary>
/// The relying-party side of this API's passkey ceremonies. The rest of the WebAuthn posture —
/// user verification required, the challenge size, the authenticator timeout — is
/// <see cref="Microsoft.AspNetCore.Identity.IdentityPasskeyOptions"/>'s own safe defaults, kept
/// rather than re-declared: what a deployment must be able to set is the domain a credential is
/// scoped to, and how long a ceremony's state may travel before it is refused.
/// </summary>
public sealed class PasskeyOptions
{
    public const string SectionName = "Passkey";

    /// <summary>
    /// The WebAuthn relying-party id: the effective domain of this API, which a browser's
    /// <c>navigator.credentials.*</c> calls require their origin to be a subdomain of.
    /// <para>
    /// <c>null</c> — the default — derives it from the request's host, which is right whenever
    /// the API serves its own domain; a deployment behind a proxy or on a subdomain of the domain
    /// its clients call sets it, exactly once for every credential ever registered: a credential
    /// is scoped to the domain it was created under, so changing this invalidates every passkey
    /// the accounts hold.
    /// </para>
    /// </summary>
    public string? ServerDomain { get; set; }

    /// <summary>
    /// How long the state string a ceremony's first call hands out stays redeemable. The state is
    /// sealed by a time-limited data protector and spent on its first presentation, so this bounds
    /// how long a captured one is worth anything, and how long its spent record is kept.
    /// <para>
    /// Five minutes, the authenticator timeout Identity writes into the options it builds: a shorter
    /// lifetime would refuse a ceremony the browser is still allowed to finish, and a longer one
    /// only widens the window for a state nobody is still using.
    /// </para>
    /// </summary>
    public TimeSpan CeremonyLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Refuses a <see cref="PasskeyOptions.ServerDomain"/> that is not a bare domain: a WebAuthn
/// relying-party id is one, and anything else — a scheme, a port, a path — would fail every
/// ceremony at the client, after the account holder had already been asked for their fingerprint.
/// Bounds <see cref="PasskeyOptions.CeremonyLifetime"/> as the two-factor challenge's lifetime is
/// bounded.
/// </summary>
internal sealed class PasskeyOptionsValidator : IValidateOptions<PasskeyOptions>
{
    private static readonly TimeSpan _minimumCeremonyLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _maximumCeremonyLifetime = TimeSpan.FromMinutes(30);

    public ValidateOptionsResult Validate(string? name, PasskeyOptions options)
    {
        var failures = new List<string>();

        // A relying-party id is a bare domain. Uri parses one as absolute with an http scheme, so a real URL such as
        // "https://api.example.org" would slip past a looser check and become a domain only at the ceremony, after
        // the client had been asked for the authenticator's user check.
        if (options.ServerDomain is { } domain
            && (string.IsNullOrWhiteSpace(domain) || domain.Contains(' ') || domain.Contains('/') || domain.Contains(':')))
        {
            failures.Add(
                $"'{PasskeyOptions.SectionName}:ServerDomain' must be a bare domain such as 'api.example.org' " +
                "— the WebAuthn relying-party id — with no scheme, port or path.");
        }

        if (options.CeremonyLifetime < _minimumCeremonyLifetime || options.CeremonyLifetime > _maximumCeremonyLifetime)
        {
            failures.Add(
                $"'{PasskeyOptions.SectionName}:CeremonyLifetime' must be between " +
                $"{_minimumCeremonyLifetime} and {_maximumCeremonyLifetime}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
