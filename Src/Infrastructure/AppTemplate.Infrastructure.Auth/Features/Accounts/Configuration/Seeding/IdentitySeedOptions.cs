using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Seeding;

public sealed class IdentitySeedOptions
{
    public const string SectionName = "IdentitySeed";

    public bool Enabled { get; set; }

    public string AdminEmail { get; set; } = string.Empty;

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
