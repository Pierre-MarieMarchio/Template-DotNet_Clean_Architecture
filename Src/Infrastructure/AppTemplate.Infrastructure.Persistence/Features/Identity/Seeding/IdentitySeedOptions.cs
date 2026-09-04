using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;

/// <summary>
/// Development-only seeding of an administrator account. Off unless switched on explicitly, and the
/// password has no default: <see cref="IdentitySeedOptionsValidator"/> requires it once seeding is
/// enabled, so a deployment cannot end up with a known, guessable administrator credential by
/// omission.
/// <para>
/// It lives beside the seeder rather than with the identity module's other options, because it configures
/// seeding — which is a persistence concern — and not authentication policy. The section name is
/// unchanged, so no configuration file, environment variable or deployment moves.
/// </para>
/// </summary>
public sealed class IdentitySeedOptions
{
    public const string SectionName = "IdentitySeed";

    /// <summary>Opt-in. Combined with a Development-only guard in <c>IdentitySeeder</c>.</summary>
    public bool Enabled { get; set; }

    public string AdminUserName { get; set; } = "administrator";

    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>No default, by design. Supply it through user secrets or an environment variable.</summary>
    public string AdminPassword { get; set; } = string.Empty;
}

internal sealed class IdentitySeedOptionsValidator : IValidateOptions<IdentitySeedOptions>
{
    public ValidateOptionsResult Validate(string? name, IdentitySeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AdminUserName))
        {
            failures.Add($"'{IdentitySeedOptions.SectionName}:AdminUserName' is required when seeding is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminEmail))
        {
            failures.Add($"'{IdentitySeedOptions.SectionName}:AdminEmail' is required when seeding is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            failures.Add(
                $"'{IdentitySeedOptions.SectionName}:AdminPassword' is required when seeding is enabled and has " +
                "no default. Set it through user secrets or an environment variable.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
