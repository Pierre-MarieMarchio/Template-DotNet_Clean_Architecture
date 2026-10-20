using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;

public sealed class ExternalIdentityOptions
{
    public const string SectionName = "ExternalIdentity";

    internal static readonly TimeSpan MinimumKeySetLifetime = TimeSpan.FromMinutes(1);

    internal static readonly TimeSpan MaximumKeySetLifetime = TimeSpan.FromHours(24);

    public IList<ExternalIdentityProviderOptions> Providers { get; } = [];

    public TimeSpan KeySetLifetime { get; set; } = TimeSpan.FromMinutes(15);

    internal ExternalIdentityProviderOptions? Find(string name) =>
        Providers.FirstOrDefault(
            provider => string.Equals(provider.Name, name, StringComparison.OrdinalIgnoreCase));
}

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
