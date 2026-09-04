using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Options;

/// <summary>Lifetime of the opaque refresh token. Its size and hashing are not configurable.</summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    public int LifetimeInDays { get; set; } = 7;
}

internal sealed class RefreshTokenOptionsValidator : IValidateOptions<RefreshTokenOptions>
{
    public ValidateOptionsResult Validate(string? name, RefreshTokenOptions options) =>
        options.LifetimeInDays is < 1 or > 90
            ? ValidateOptionsResult.Fail(
                $"'{RefreshTokenOptions.SectionName}:LifetimeInDays' must be between 1 and 90.")
            : ValidateOptionsResult.Success;
}
