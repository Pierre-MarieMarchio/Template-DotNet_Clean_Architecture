using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.ExternalIdentity.Options;

/// <summary>
/// The external OpenID Connect provider this API accepts access tokens from, and how a token's claims
/// become a caller.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class AuthenticationExternalOptions
{
    /// <summary>The configuration section this type is bound from.</summary>
    public const string SectionName = "Authentication:External";

    /// <summary>
    /// The provider's issuer URL. Its discovery document supplies the signing keys and the issuer a
    /// token must name.
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>The audience a token must be issued for: this API's identifier at the provider.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Refuses an <c>http</c> authority. Off only for a provider on a developer's machine: the
    /// discovery document is where the signing keys come from.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>The claim naming the provider's subject. Mapped to an internal owner, never used as one.</summary>
    public string SubjectClaim { get; set; } = "sub";

    /// <summary>The claim carrying the caller's email address, kept for notifications.</summary>
    public string EmailClaim { get; set; } = "email";

    /// <summary>The claim carrying the caller's display name, kept for notifications.</summary>
    public string NameClaim { get; set; } = "name";

    /// <summary>Where a caller's roles come from.</summary>
    public RolesOptions Roles { get; set; } = new();

    /// <summary>Where a caller's roles come from, and which one administers.</summary>
    public sealed class RolesOptions
    {
        /// <summary>
        /// <see cref="RoleSource.Local"/> by default: no role is taken from a token until the
        /// deployment says which claim carries them.
        /// </summary>
        public RoleSource Source { get; set; } = RoleSource.Local;

        /// <summary>
        /// A dot path into the token naming a string or an array of strings — <c>roles</c> for Entra
        /// ID, <c>realm_access.roles</c> for Keycloak, a namespaced claim for Auth0. Required when
        /// <see cref="Source"/> is <see cref="RoleSource.Claim"/>.
        /// </summary>
        public string? ClaimPath { get; set; }

        /// <summary>The role the <c>Administrator</c> policy requires.</summary>
        public string AdministratorRole { get; set; } = "Admin";
    }

    /// <summary>The two places a role can be read from.</summary>
    public enum RoleSource
    {
        /// <summary>The provider's token, at <see cref="RolesOptions.ClaimPath"/>.</summary>
        Claim = 1,

        /// <summary>This application's own role-assignment table.</summary>
        Local = 2,
    }
}

internal sealed class AuthenticationExternalOptionsValidator : IValidateOptions<AuthenticationExternalOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthenticationExternalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        const string section = AuthenticationExternalOptions.SectionName;

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority)
            || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"'{section}:{nameof(options.Authority)}' must be the provider's absolute issuer URL.");
        }
        else if (options.RequireHttpsMetadata && authority.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add(
                $"'{section}:{nameof(options.Authority)}' is not https, which " +
                $"'{section}:{nameof(options.RequireHttpsMetadata)}' requires: the signing keys are fetched from it.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"'{section}:{nameof(options.Audience)}' is required: a token issued for another API must be refused.");
        }

        RequireClaimName(failures, $"{section}:{nameof(options.SubjectClaim)}", options.SubjectClaim);
        RequireClaimName(failures, $"{section}:{nameof(options.EmailClaim)}", options.EmailClaim);
        RequireClaimName(failures, $"{section}:{nameof(options.NameClaim)}", options.NameClaim);

        var roles = options.Roles;
        string rolesSection = $"{section}:{nameof(options.Roles)}";

        if (roles is null)
        {
            failures.Add($"'{rolesSection}' cannot be null.");
        }
        else
        {
            if (!Enum.IsDefined(roles.Source))
            {
                failures.Add($"'{rolesSection}:{nameof(roles.Source)}' must be Claim or Local.");
            }
            else if (roles.Source == AuthenticationExternalOptions.RoleSource.Claim
                && !IsClaimPath(roles.ClaimPath))
            {
                failures.Add(
                    $"'{rolesSection}:{nameof(roles.ClaimPath)}' is required when roles come from the token: " +
                    "a dot path such as 'roles' or 'realm_access.roles', with no empty segment.");
            }

            if (string.IsNullOrWhiteSpace(roles.AdministratorRole))
            {
                failures.Add($"'{rolesSection}:{nameof(roles.AdministratorRole)}' is required.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireClaimName(List<string> failures, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"'{key}' must name a claim.");
        }
    }

    private static bool IsClaimPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.StartsWith('.')
        && !path.EndsWith('.')
        && !path.Contains("..", StringComparison.Ordinal);
}
