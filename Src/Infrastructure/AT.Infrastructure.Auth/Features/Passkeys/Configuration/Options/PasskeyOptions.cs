using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;

public sealed class PasskeyOptions
{
    public const string SectionName = "Passkey";

    public string? ServerDomain { get; set; }

    public TimeSpan CeremonyLifetime { get; set; } = TimeSpan.FromMinutes(5);
}

internal sealed class PasskeyOptionsValidator : IValidateOptions<PasskeyOptions>
{
    private static readonly TimeSpan _minimumCeremonyLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _maximumCeremonyLifetime = TimeSpan.FromMinutes(30);

    public ValidateOptionsResult Validate(string? name, PasskeyOptions options)
    {
        var failures = new List<string>();

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
