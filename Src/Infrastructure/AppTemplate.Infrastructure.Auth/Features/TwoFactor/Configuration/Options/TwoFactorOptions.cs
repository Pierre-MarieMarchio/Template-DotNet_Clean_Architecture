using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.TwoFactor.Configuration.Options;

public sealed class TwoFactorOptions
{
    public const string SectionName = "TwoFactor";

    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public int RecoveryCodeCount { get; set; } = 10;

    public int MaxChallengeAttempts { get; set; } = 5;

    public string Issuer { get; set; } = "AppTemplate";
}

internal sealed class TwoFactorOptionsValidator : IValidateOptions<TwoFactorOptions>
{
    private static readonly TimeSpan _minimumChallengeLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _maximumChallengeLifetime = TimeSpan.FromMinutes(30);

    public ValidateOptionsResult Validate(string? name, TwoFactorOptions options)
    {
        var failures = new List<string>();

        if (options.ChallengeLifetime < _minimumChallengeLifetime || options.ChallengeLifetime > _maximumChallengeLifetime)
        {
            failures.Add(
                $"'{TwoFactorOptions.SectionName}:ChallengeLifetime' must be between " +
                $"{_minimumChallengeLifetime} and {_maximumChallengeLifetime}.");
        }

        if (options.RecoveryCodeCount is < 1 or > 20)
        {
            failures.Add($"'{TwoFactorOptions.SectionName}:RecoveryCodeCount' must be between 1 and 20.");
        }

        if (options.MaxChallengeAttempts is < 1 or > 20)
        {
            failures.Add(
                $"'{TwoFactorOptions.SectionName}:MaxChallengeAttempts' must be between 1 and 20. It is " +
                "the only bound on guessing a code, so a large value is a decision rather than a default.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add($"'{TwoFactorOptions.SectionName}:Issuer' is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
