using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.ExternalIdentity.Configuration.Options;

public sealed class AuthenticationExternalOptions
{
    public const string SectionName = "Authentication:External";

    public string Authority { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    public string SubjectClaim { get; set; } = "sub";

    public string EmailClaim { get; set; } = "email";

    public string NameClaim { get; set; } = "name";

    public RolesOptions Roles { get; set; } = new();

    public sealed class RolesOptions
    {
        public RoleSource Source { get; set; } = RoleSource.Local;

        public string? ClaimPath { get; set; }

        public string AdministratorRole { get; set; } = "Admin";
    }

    public enum RoleSource
    {
        Claim = 1,

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
