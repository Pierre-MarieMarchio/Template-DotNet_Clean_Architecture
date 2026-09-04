namespace AppTemplate.Api.Common.Security;

/// <summary>
/// The fixed-window length both rate-limiting policies use.
/// </summary>
/// <remarks>
/// A registered default rather than a literal inside <see cref="RateLimitingPolicies"/>, so a host
/// that cannot tolerate the built-in limiter's real-time window — the fixed-window limiter exposes
/// no injectable clock, so this is the only lever available to a caller that needs one — can replace
/// this singleton before the limiter is built, the same way <c>AddInMemoryModule</c> replaces the
/// clock and the email sender. Nothing in this project does; that is a decision for whatever host
/// composes this differently.
/// </remarks>
public sealed record RateLimiterWindow(TimeSpan Duration)
{
    public static readonly RateLimiterWindow Default = new(TimeSpan.FromMinutes(1));
}
