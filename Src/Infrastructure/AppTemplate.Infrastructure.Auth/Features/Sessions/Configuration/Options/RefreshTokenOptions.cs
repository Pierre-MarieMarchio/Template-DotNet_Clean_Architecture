using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;

public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    public int LifetimeInDays { get; set; } = 7;

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
