using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    //#if (localAuth)
    public int AuthenticationPermitLimit { get; set; } = 10;
    //#endif

    public int GlobalPermitLimit { get; set; } = 300;
}

internal sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    private static readonly TimeSpan _minWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maxWindow = TimeSpan.FromDays(1);

    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.Window < _minWindow || options.Window > _maxWindow)
        {
            failures.Add(
                $"'{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.Window)}' " +
                $"must be between {_minWindow} and {_maxWindow}.");
        }

        //#if (localAuth)
        if (options.AuthenticationPermitLimit < 1)
        {
            failures.Add(
                $"'{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.AuthenticationPermitLimit)}' " +
                "must be at least 1.");
        }
        //#endif

        if (options.GlobalPermitLimit < 1)
        {
            failures.Add(
                $"'{RateLimitingOptions.SectionName}:{nameof(RateLimitingOptions.GlobalPermitLimit)}' " +
                "must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
