using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Security;

/// <summary>
/// How many requests one client address may make per window.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class RateLimitingOptions
{
    /// <summary>The configuration section this type is bound from.</summary>
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// One minute by default, for both limits. The fixed-window limiter reads the wall clock and
    /// exposes no injectable clock, so a test host widens this rather than faking time.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    //#if (localAuth)
    /// <summary>
    /// 10 by default, applied to the authentication controller. Together with account lockout this
    /// is what bounds online password guessing.
    /// </summary>
    public int AuthenticationPermitLimit { get; set; } = 10;
    //#endif

    /// <summary>300 by default, applied to every request before any endpoint is reached.</summary>
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
