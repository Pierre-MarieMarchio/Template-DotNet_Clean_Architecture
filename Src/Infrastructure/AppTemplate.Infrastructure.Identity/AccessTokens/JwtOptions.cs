using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Identity.AccessTokens;

/// <summary>
/// Access-token signing and validation settings. Bound and validated at startup: the previous
/// <c>JwtSettings</c> was a concrete singleton with non-nullable strings behind a blanket
/// <c>#pragma warning disable</c>, so a missing key surfaced only as <c>IDX10653</c> the first time
/// somebody tried to log in.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 requires a key at least as long as its output. Anything shorter is rejected.</summary>
    public const int MinimumKeyLengthInBytes = 32;

    /// <summary>Symmetric signing key. Supply it through a secret store, never through appsettings.json.</summary>
    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Defaults to <c>true</c>; only a local development host has any business turning it off.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Deliberately short. An access token cannot be revoked, so its lifetime is the window during
    /// which a stolen one remains useful; the refresh token is what provides continuity.
    /// </summary>
    public int AccessTokenLifetimeInMinutes { get; set; } = 15;

    internal SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Key))
        {
            failures.Add($"'{JwtOptions.SectionName}:Key' is required.");
        }
        else if (Encoding.UTF8.GetByteCount(options.Key) < JwtOptions.MinimumKeyLengthInBytes)
        {
            failures.Add(
                $"'{JwtOptions.SectionName}:Key' must be at least {JwtOptions.MinimumKeyLengthInBytes} bytes " +
                "long to sign HS256 tokens.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"'{JwtOptions.SectionName}:Issuer' is required; issuer validation is never disabled.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add($"'{JwtOptions.SectionName}:Audience' is required; audience validation is never disabled.");
        }

        if (options.AccessTokenLifetimeInMinutes is < 1 or > 1440)
        {
            failures.Add($"'{JwtOptions.SectionName}:AccessTokenLifetimeInMinutes' must be between 1 and 1440.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
