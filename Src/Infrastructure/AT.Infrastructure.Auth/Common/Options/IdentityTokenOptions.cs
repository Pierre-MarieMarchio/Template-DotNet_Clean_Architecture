using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Common.Options;

public sealed class IdentityTokenOptions
{
    public const string SectionName = "IdentityTokens";

    public TimeSpan Lifespan { get; set; } = TimeSpan.FromDays(1);
}

internal sealed class IdentityTokenOptionsValidator : IValidateOptions<IdentityTokenOptions>
{
    private static readonly TimeSpan _minimumLifespan = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _maximumLifespan = TimeSpan.FromDays(30);

    public ValidateOptionsResult Validate(string? name, IdentityTokenOptions options) =>
        options.Lifespan < _minimumLifespan || options.Lifespan > _maximumLifespan
            ? ValidateOptionsResult.Fail(
                $"'{IdentityTokenOptions.SectionName}:Lifespan' must be between {_minimumLifespan} and " +
                $"{_maximumLifespan}.")
            : ValidateOptionsResult.Success;
}
