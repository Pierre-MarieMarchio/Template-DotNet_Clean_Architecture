using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public const int MinimumKeyLengthInBytes = 32;

    public const string DevelopmentKeyPrefix = "dev-only";

    public string Key { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    public int AccessTokenLifetimeInMinutes { get; set; } = 15;

    internal SymmetricSecurityKey CreateSigningKey() => new(Encoding.UTF8.GetBytes(Key));
}

internal sealed class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
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
        else if (!environment.IsDevelopment()
            && options.Key.StartsWith(JwtOptions.DevelopmentKeyPrefix, StringComparison.Ordinal))
        {
            failures.Add(
                $"'{JwtOptions.SectionName}:Key' is the development key the template ships, which is public. " +
                $"Outside Development, supply a secret of your own as '{JwtOptions.SectionName}:Key' " +
                "(environment variable Jwt__Key, or JWT_KEY in .env for Docker Compose).");
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
