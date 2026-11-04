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

    public IList<string> PreviousKeys { get; } = [];

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    public int AccessTokenLifetimeInMinutes { get; set; } = 15;

    internal SymmetricSecurityKey CreateSigningKey() => CreateKey(Key);

    internal IEnumerable<SymmetricSecurityKey> CreateValidationKeys() =>
        [CreateSigningKey(), .. PreviousKeys.Select(CreateKey)];

    private static SymmetricSecurityKey CreateKey(string key) => new(Encoding.UTF8.GetBytes(key));
}

internal sealed class JwtOptionsValidator(IHostEnvironment environment) : IValidateOptions<JwtOptions>
{
    private const string _developmentKeyRemedy =
        $"Outside Development, supply a secret of your own as '{JwtOptions.SectionName}:Key' " +
        "(environment variable Jwt__Key, or JWT_KEY in .env for Docker Compose).";

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        var key = $"{JwtOptions.SectionName}:Key";

        if (string.IsNullOrWhiteSpace(options.Key))
        {
            failures.Add($"'{key}' is required.");
        }
        else if (KeyFailure(key, options.Key, _developmentKeyRemedy) is { } failure)
        {
            failures.Add(failure);
        }

        failures.AddRange(PreviousKeyFailures(options));

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

    private IEnumerable<string> PreviousKeyFailures(JwtOptions options)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int index = 0; index < options.PreviousKeys.Count; index++)
        {
            string previous = options.PreviousKeys[index];
            var name = $"{JwtOptions.SectionName}:{nameof(JwtOptions.PreviousKeys)}:{index}";

            if (string.IsNullOrWhiteSpace(previous))
            {
                yield return $"'{name}' is empty; remove it from '{JwtOptions.SectionName}:{nameof(JwtOptions.PreviousKeys)}'.";
            }
            else if (string.Equals(previous, options.Key, StringComparison.Ordinal))
            {
                yield return
                    $"'{name}' is the current '{JwtOptions.SectionName}:Key'; a previous key is one tokens are " +
                    "no longer signed with.";
            }
            else if (!seen.Add(previous))
            {
                yield return $"'{name}' repeats an earlier previous key.";
            }
            else if (KeyFailure(name, previous, "Remove it: a token it signed proves nothing.") is { } failure)
            {
                yield return failure;
            }
        }
    }

    private string? KeyFailure(string name, string key, string developmentKeyRemedy)
    {
        if (Encoding.UTF8.GetByteCount(key) < JwtOptions.MinimumKeyLengthInBytes)
        {
            return $"'{name}' must be at least {JwtOptions.MinimumKeyLengthInBytes} bytes long to sign HS256 tokens.";
        }

        if (!environment.IsDevelopment()
            && key.StartsWith(JwtOptions.DevelopmentKeyPrefix, StringComparison.Ordinal))
        {
            return $"'{name}' is the development key the template ships, which is public. {developmentKeyRemedy}";
        }

        return null;
    }
}
