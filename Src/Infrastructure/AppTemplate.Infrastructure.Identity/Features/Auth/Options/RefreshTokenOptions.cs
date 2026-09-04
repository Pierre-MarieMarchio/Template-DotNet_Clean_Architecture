using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Options;

/// <summary>Lifetime of the opaque refresh token. Its size and hashing are not configurable.</summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    public int LifetimeInDays { get; set; } = 7;

    /// <summary>
    /// How long an expired grant is kept before the purge sweep deletes it. <c>ReplacedByTokenHash</c>
    /// is what makes a replay of an already-rotated token detectable; purging a row the moment it
    /// expires would erase that evidence just as fast as it erases the row itself. Kept well past a
    /// live grant's own lifetime so a slow client presenting a stale token still gets told "replay"
    /// rather than "unknown".
    /// </summary>
    public int RetentionInDays { get; set; } = 7;
}

internal sealed class RefreshTokenOptionsValidator : IValidateOptions<RefreshTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, RefreshTokenOptions options)
    {
        var failures = new List<string>();

        if (options.LifetimeInDays is < 1 or > 90)
        {
            failures.Add($"'{RefreshTokenOptions.SectionName}:LifetimeInDays' must be between 1 and 90.");
        }

        if (options.RetentionInDays is < 1 or > 90)
        {
            failures.Add($"'{RefreshTokenOptions.SectionName}:RetentionInDays' must be between 1 and 90.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
