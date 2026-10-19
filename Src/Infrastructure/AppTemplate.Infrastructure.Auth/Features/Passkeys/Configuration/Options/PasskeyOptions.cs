using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;

/// <summary>
/// The relying-party side of this API's passkey ceremonies. The rest of the WebAuthn posture —
/// user verification required, the challenge size, the authenticator timeout — is
/// <see cref="Microsoft.AspNetCore.Identity.IdentityPasskeyOptions"/>'s own safe defaults, kept
/// rather than re-declared: what a deployment must be able to set is the domain a credential is
/// scoped to, and that is the one member here.
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
}

/// <summary>
/// Refuses a <see cref="PasskeyOptions.ServerDomain"/> that is not a bare domain: a WebAuthn
/// relying-party id is one, and anything else — a scheme, a port, a path — would fail every
/// ceremony at the client, after the account holder had already been asked for their fingerprint.
/// </summary>
internal sealed class PasskeyOptionsValidator : IValidateOptions<PasskeyOptions>
{
    public ValidateOptionsResult Validate(string? name, PasskeyOptions options)
    {
        var failures = new List<string>();

        // A relying-party id is a bare domain. Uri parses one as absolute with an http scheme, so
        // a real URL here is what would slip a scheme past a looser check: "https://api.example.org"
        // would become the domain "api.example.org" only at the ceremony, after the client had
        // already been asked for the authenticator's user check.
        if (options.ServerDomain is { } domain
            && (string.IsNullOrWhiteSpace(domain) || domain.Contains(' ') || domain.Contains('/') || domain.Contains(':')))
        {
            failures.Add(
                $"'{PasskeyOptions.SectionName}:ServerDomain' must be a bare domain such as 'api.example.org' " +
                "— the WebAuthn relying-party id — with no scheme, port or path.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
